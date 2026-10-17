using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Accounts;
using HoneyDrunk.Identity.Client;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Tests.Fixtures;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HoneyDrunk.Identity.Tests.Accounts;

/// <summary>Exercises real Auth, HTTP, SQL and Audit integration at the service boundary.</summary>
public sealed class IdentityBoundaryTests(SqlServerFixture sql) : IAsyncLifetime, IClassFixture<SqlServerFixture>
{
    private readonly string connection = sql.NewDatabaseConnection();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await using var db = Context();
        await DatabaseSchema.DeployAsync(db.Database);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await using var db = Context();
        await db.Database.EnsureDeletedAsync();
    }

    /// <summary>Verifies HTTP resolution survives restart without duplicate users or forged audit attribution.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task Resolve_WithValidToken_PersistsStableUserAndTrustedAudit()
    {
        UserRecord first;
        using (var host = new IdentityTestHost(connection))
        using (var http = host.Client())
        {
            http.DefaultRequestHeaders.Add("X-Tenant-Id", "01ARZ3NDEKTSV4RRFFQ69G5FAV");
            http.DefaultRequestHeaders.Add("X-Correlation-Id", "forged");
            http.DefaultRequestHeaders.Add("X-Baggage-Owner", "forged");
            http.DefaultRequestHeaders.Add("baggage", "owner=forged");
            first = (await http.GetFromJsonAsync<UserRecord>("/users/me"))!;
            Assert.StartsWith("usr_", first.UserId);
        }

        using var restarted = new IdentityTestHost(connection);
        using var client = restarted.Client();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        Assert.Equal(first, await new IdentityClient(client).Resolve(token));
        await using var db = Context();
        Assert.Equal(1, await db.Users.CountAsync());
        var records = await db.Audit.ToListAsync();
        Assert.All(records, entry =>
        {
            Assert.Equal(TenantId.Internal.ToString(), entry.TenantId);
            Assert.NotEqual("forged", entry.CorrelationId);
            Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId));
        });
        Assert.Single(records, entry => entry.EventName == "identity.user.created");
    }

    /// <summary>Verifies invalid tokens cannot create an account.</summary>
    /// <param name="failure">The token defect.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("subject")]
    [InlineData("scope")]
    [InlineData("wrongScope")]
    [InlineData("malformed")]
    [InlineData("missing")]
    public async Task Resolve_WithInvalidToken_RejectsWithoutCreatingUser(string failure)
    {
        using var host = new IdentityTestHost(connection);
        using var client = host.Client(audience: failure == "audience" ? "wrong" : "identity", issuer: failure == "issuer" ? "https://wrong.test" : "https://identity.test", wrongKey: failure == "signature", expired: failure == "expired", includeSubject: failure != "subject", scope: failure == "scope" ? null : failure == "wrongScope" ? "access_as_user_extra" : "access_as_user");
        if (failure == "malformed")
            client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid.token.value");
        if (failure == "missing")
            client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/users/me", UriKind.Relative))).StatusCode);
        await using var db = Context();
        Assert.Empty(await db.Users.ToListAsync());
    }

    /// <summary>Verifies an inactive account cannot be re-created by another login.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task Resolve_WithDisabledAccount_RejectsExistingMapping()
    {
        using var host = new IdentityTestHost(connection);
        using var client = host.Client();
        var user = (await client.GetFromJsonAsync<UserRecord>("/users/me"))!;
        await using var db = Context();
        var row = await db.Users.SingleAsync();
        row.State = "Disabled";
        await db.SaveChangesAsync();
        Assert.Null(await new IdentityClient(client).Resolve(client.DefaultRequestHeaders.Authorization!.Parameter!));
        Assert.Equal(user.UserId, (await db.Users.SingleAsync()).UserId);
        Assert.Equal(1, await db.Subjects.CountAsync());
    }

    /// <summary>Verifies readiness and public configuration accurately describe local setup.</summary>
    /// <param name="configured">Whether all public provider settings are present.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configuration_WithProviderSettings_ReportsActualReadiness(bool configured)
    {
        using var host = new IdentityTestHost(connection, configured);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
        var response = await client.GetAsync(new Uri("/client-configuration", UriKind.Relative));
        Assert.Equal(configured ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        if (configured)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("native-client", body.GetProperty("clientId").GetString());
            Assert.Equal("api://identity/access_as_user", body.GetProperty("scope").GetString());
        }
    }

    /// <summary>Verifies an audit storage failure cannot leave a partially created identity.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task Resolve_WhenAuditWriteFails_RollsBackDirectoryAndAllowsRetry()
    {
        await using var db = Context();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectIdentityAudit ON AuditRecords AFTER INSERT AS BEGIN THROW 51001, 'Injected test failure', 1; END;");
        await using (var failing = Context())
            await Assert.ThrowsAsync<DbUpdateException>(() => new SqlUserDirectory(failing, TimeProvider.System, new TestExternalAccounts()).Resolve(new("https://identity.test", "alice")));
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Subjects.CountAsync());
        Assert.Equal(0, await db.Audit.CountAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectIdentityAudit;");
        await new SqlUserDirectory(db, TimeProvider.System, new TestExternalAccounts()).Resolve(new("https://identity.test", "alice"));
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Audit.CountAsync());
    }

    private IdentityDbContext Context() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection).Options);
}
