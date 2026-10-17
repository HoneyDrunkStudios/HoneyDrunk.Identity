using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;

namespace HoneyDrunk.Identity.Tests.AccountLifecycle;

/// <summary>HTTP authorization and recent-proof boundary tests using real JWT validation and SQL.</summary>
public sealed class LifecycleBoundaryTests(SqlServerFixture sql) : IAsyncLifetime, IClassFixture<SqlServerFixture>
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

    /// <summary>Fresh issuance without auth_time is rejected; owner confirmation and explicit fresh recovery are required.</summary>
    /// <returns>The HTTP acceptance test.</returns>
    [Fact]
    public async Task RecentOwnerProofAndExplicitRecoveryAreRequired()
    {
        using var host = new IdentityTestHost(connection);
        using var stale = host.Client("alice");
        var owner = await stale.GetFromJsonAsync<UserRecord>("/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.PostAsJsonAsync("/users/me/deletion", new { confirmed = true })).StatusCode);
        using var fresh = host.Client("alice", authenticatedAt: DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.BadRequest, (await fresh.PostAsJsonAsync("/users/me/deletion", new { confirmed = false })).StatusCode);
        var requested = await fresh.PostAsJsonAsync("/users/me/deletion", new { confirmed = true });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.Equal(owner!.UserId, (await requested.Content.ReadFromJsonAsync<AccountStatus>())!.UserId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.GetAsync(new Uri("/users/me", UriKind.Relative))).StatusCode);
        Assert.Equal("Inactive", (await fresh.GetFromJsonAsync<AccountStatus>("/users/me/status"))!.State);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.PostAsJsonAsync("/users/me/recovery", new { confirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsJsonAsync("/users/me/recovery", new { confirmed = true })).StatusCode);
        Assert.Equal(owner.UserId, (await fresh.GetFromJsonAsync<UserRecord>("/users/me"))!.UserId);
        using var untrusted = host.Client("alice", wrongKey: true, authenticatedAt: DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, (await untrusted.PostAsJsonAsync("/users/me/deletion", new { confirmed = true })).StatusCode);
        using var anonymous = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/users/me/deletion", new { confirmed = true, userId = owner.UserId })).StatusCode);
    }

    /// <summary>A valid second token without delegated permission cannot link or unlink accounts.</summary>
    /// <returns>The HTTP authorization test.</returns>
    [Fact]
    public async Task AdditionalProofRequiresDelegatedScope()
    {
        using var host = new IdentityTestHost(connection);
        using var owner = host.Client("alice", authenticatedAt: DateTimeOffset.UtcNow);
        using var additional = host.Client("bob", authenticatedAt: DateTimeOffset.UtcNow, scope: null);
        var proof = new { accessToken = additional.DefaultRequestHeaders.Authorization!.Parameter! };
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsJsonAsync("/users/me/link", proof)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsJsonAsync("/users/me/unlink", proof)).StatusCode);
        await using var db = Context();
        Assert.Empty(await db.Subjects.ToListAsync());
    }

    private IdentityDbContext Context() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection).Options);
}
