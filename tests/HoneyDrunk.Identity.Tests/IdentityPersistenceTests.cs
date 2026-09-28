using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Audit.Data;
using HoneyDrunk.Identity.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HoneyDrunk.Identity.Tests;

/// <summary>Executable regression coverage for identity persistence tests.</summary>
public sealed class IdentityPersistenceTests(SqlServerFixture sql) : IAsyncLifetime, IClassFixture<SqlServerFixture>
{
    private readonly string connection = sql.NewDatabaseConnection();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    /// <summary>Verifies audit preserves canonical envelope and does not purge old records on append.</summary>
    /// <returns>A task completing after the regression checks.</returns>
    [Fact]
    public async Task AuditPreservesCanonicalEnvelopeAndDoesNotPurgeOldRecordsOnAppend()
    {
        var entry = new AuditEntry(
            AuditEntryId.New(),
            DateTimeOffset.UtcNow.AddYears(-1),
            "usr_test",
            "identity.test",
            AuditCategory.Security,
            AuditOutcome.Denied,
            new AuditTarget("user", "usr_test", "Test account"),
            TenantId.Internal,
            "trace-test",
            AuditOperation.Update,
            [new AuditChange("state", "Active", "Disabled")],
            new Dictionary<string, string> { ["policy"] = "test" },
            "Explicit test reason");
        await using (var context = CreateContext())
        await using (var unitOfWork = new IdentityAuditUnitOfWork(context))
        {
            var audit = new DataAuditLog(unitOfWork, NullLogger<DataAuditLog>.Instance);
            await audit.AppendAsync(entry);
            await audit.AppendAsync(entry with { Id = AuditEntryId.New(), OccurredAt = DateTimeOffset.UtcNow });
        }

        await using var restored = CreateContext();
        Assert.Equal(2, await restored.Audit.CountAsync());
        var actual = (await restored.Audit.SingleAsync(row => row.Id == entry.Id.ToString())).ToEntry();
        Assert.Equal(entry with { Changes = actual.Changes, Metadata = actual.Metadata }, actual);
        Assert.Equal(entry.Changes, actual.Changes);
        Assert.Equal(entry.Metadata, actual.Metadata);
    }

    /// <summary>Verifies concurrent resolution is stable across restart and issuer boundaries.</summary>
    /// <returns>A task completing after the regression checks.</returns>
    [Fact]
    public async Task ConcurrentResolutionIsStableAcrossRestartAndIssuerBoundaries()
    {
        var subject = new ExternalSubject("https://issuer.test", "subject");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = CreateContext();
            return await new SqlUserDirectory(context, TimeProvider.System).Resolve(subject);
        }));
        Assert.Single(results.Select(result => result.UserId).Distinct());
        await using var restarted = CreateContext();
        var directory = new SqlUserDirectory(restarted, TimeProvider.System);
        Assert.Equal(results[0].UserId, (await directory.Resolve(subject)).UserId);
        Assert.NotEqual(results[0].UserId, (await directory.Resolve(subject with { Issuer = "https://another.test" })).UserId);
        Assert.Equal(2, await restarted.Users.CountAsync());
        Assert.Equal(2, await restarted.Audit.CountAsync(row => row.EventName == "identity.user.created"));
    }

    private IdentityDbContext CreateContext() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection).Options);
}
