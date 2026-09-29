using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Abstractions.AccountLifecycle;
using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Identity.Accounts;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HoneyDrunk.Identity.Tests.AccountLifecycle;

/// <summary>Real-SQL lifecycle, isolation, recovery, retry and restore acceptance coverage.</summary>
public sealed class AccountLifecycleTests(SqlServerFixture sql) : IAsyncLifetime, IClassFixture<SqlServerFixture>
{
    private readonly string connection = sql.NewDatabaseConnection();
    private readonly TestClock clock = new();
    private readonly TestExternalAccounts external = new();
    private readonly ExternalSubject alice = new("https://identity.test", "alice", Guid.NewGuid().ToString());
    private readonly ExternalSubject bob = new("https://identity.test", "bob", Guid.NewGuid().ToString());

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

    /// <summary>Recovery requires fresh proof, explicit confirmation and the original unextended deadline.</summary>
    /// <returns>The SQL acceptance test.</returns>
    [Fact]
    public async Task RecoveryIsExplicitAndDeadlineIsExclusive()
    {
        var user = await Seed(alice);
        await using (var db = Context())
        {
            var service = Service(db);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Request(new(alice, null), true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Request(new(alice, clock.Now.AddMinutes(-5)), true));
            await Assert.ThrowsAsync<ArgumentException>(() => service.Request(new(alice, clock.Now), false));
        }

        AccountStatus first;
        await using (var db = Context())
            first = await Service(db).Request(new(alice, clock.Now), true);
        Assert.Equal("Inactive", first.State);
        Assert.Equal(clock.Now.AddDays(30), first.RecoveryDeadline);
        clock.Now = clock.Now.AddDays(3);
        await using (var db = Context())
        {
            Assert.Equal(first.RecoveryDeadline, (await Service(db).Request(new(alice, clock.Now), true)).RecoveryDeadline);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SqlUserDirectory(db, clock, external).Resolve(alice));
        }

        await using (var db = Context())
            Assert.Equal("Inactive", (await Service(db).Status(alice)).State);
        await using (var db = Context())
            Assert.Equal("Active", (await Service(db).Cancel(new(alice, clock.Now), true)).State);
        await using (var db = Context())
            Assert.Equal(user.UserId, (await new SqlUserDirectory(db, clock, external).Resolve(alice)).UserId);
        await using (var db = Context())
            first = await Service(db).Request(new(alice, clock.Now), true);
        clock.Now = first.RecoveryDeadline!.Value;
        await using (var db = Context())
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).Cancel(new(alice, clock.Now), true));
    }

    /// <summary>Missing/forged/stale receipts and provider failure never claim erasure; retries purge only the owner.</summary>
    /// <returns>The SQL acceptance test.</returns>
    [Fact]
    public async Task ErasureWaitsForVerifiedFanoutAndProviderThenRetainsOnlyFiniteMarker()
    {
        var user = await Seed(alice);
        var other = await Seed(bob);
        await using (var db = Context())
        {
            db.LegacyAudit.Add(new() { ActorHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alice.Issuer, alice.Subject })))), EventName = "legacy", OccurredAt = clock.Now });
            await db.SaveChangesAsync();
            await Service(db).Request(new(alice, clock.Now), true);
        }

        clock.Now = clock.Now.AddDays(30);
        await Process(user.UserId);
        await using (var db = Context())
        {
            Assert.Equal("Erasing", (await db.Users.SingleAsync(u => u.UserId == user.UserId)).State);
            Assert.Empty(await db.Erasures.ToListAsync());
            var intent = await Intent(db, user.UserId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).Acknowledge(new(user.UserId, intent.Version, intent.Consumer, new string('0', 64))));
        }

        await using (var db = Context())
        {
            var intent = await Intent(db, user.UserId);
            await Service(db).Acknowledge(new(user.UserId, intent.Version - 1, intent.Consumer, intent.Acknowledgment));
            Assert.False((await db.Deliveries.SingleAsync()).Acknowledged);
            await Service(db).Acknowledge(new(user.UserId, intent.Version, intent.Consumer, intent.Acknowledgment));
        }

        external.Fail = true;
        clock.Now = clock.Now.AddMinutes(2);
        await Process(user.UserId);
        await using (var db = Context())
        {
            Assert.Equal("ProviderLifecycleUnavailable", (await db.Users.SingleAsync(u => u.UserId == user.UserId)).FailureCode);
            Assert.Empty(await db.Erasures.ToListAsync());
        }

        external.Fail = false;
        clock.Now = clock.Now.AddMinutes(2);
        await Process(user.UserId);
        var verified = clock.Now;
        await Process(user.UserId);
        await using (var db = Context())
        {
            Assert.Equal(other.UserId, (await db.Users.SingleAsync()).UserId);
            Assert.Single(await db.Subjects.ToListAsync());
            Assert.DoesNotContain(await db.Audit.ToListAsync(), a => a.Actor == user.UserId);
            Assert.Empty(await db.LegacyAudit.ToListAsync());
            Assert.Empty(await db.Deliveries.ToListAsync());
            Assert.Empty(await db.Set<OutboxMessage>().ToListAsync());
            Assert.Equal(verified, (await db.Erasures.SingleAsync()).ErasedAt);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SqlUserDirectory(db, clock, external).Resolve(alice));
        }

        clock.Now = verified.AddDays(35).AddTicks(-1);
        await using (var db = Context())
        {
            await Service(db).Prune();
            Assert.Single(await db.Erasures.ToListAsync());
        }

        clock.Now = verified.AddDays(35);
        await using (var db = Context())
        {
            await Service(db).Prune();
            Assert.Empty(await db.Erasures.ToListAsync());
        }
    }

    /// <summary>Expired intent capabilities are replaced, and a restored account is purged by current external markers.</summary>
    /// <returns>The SQL acceptance test.</returns>
    [Fact]
    public async Task ExpiredDeliveryAndRestoredBackupCannotResurrectErasedAccount()
    {
        var user = await Seed(alice);
        LifecycleIntent old;
        await using (var db = Context())
        {
            await Service(db).Request(new(alice, clock.Now), true);
            old = await Intent(db, user.UserId);
        }

        clock.Now = old.ExpiresAt;
        await Process(user.UserId);
        await using (var db = Context())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).Acknowledge(new(user.UserId, old.Version, old.Consumer, old.Acknowledgment)));
        }

        await using (var db = Context())
        {
            Assert.NotEqual(old.Acknowledgment, (await Intent(db, user.UserId)).Acknowledgment);
            var verified = clock.Now;
            await Service(db).ReapplyErasure(new() { UserId = user.UserId, ErasedAt = verified });
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Empty(await db.Subjects.ToListAsync());
            Assert.Empty(await db.Audit.Where(a => a.Actor == user.UserId).ToListAsync());
            Assert.Equal(verified, (await db.Erasures.SingleAsync()).ErasedAt);
        }
    }

    /// <summary>Linking needs both fresh proofs, rejects existing accounts and prevents removal of the retained method.</summary>
    /// <returns>The SQL acceptance test.</returns>
    [Fact]
    public async Task ProviderLinkingNeverMergesByEmailOrLosesTheLastMethod()
    {
        var user = await Seed(alice);
        await Seed(bob);
        var added = new ExternalSubject(alice.Issuer, "another-provider", Guid.NewGuid().ToString());
        await using (var db = Context())
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).Link(new(alice, clock.Now), new(bob, clock.Now)));
        await using (var db = Context())
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).Link(new(alice, clock.Now), new(added, null)));
        await using (var db = Context())
            Assert.Equal(user.UserId, (await Service(db).Link(new(alice, clock.Now), new(added, clock.Now))).UserId);
        await using (var db = Context())
            Assert.Equal(user.UserId, (await new SqlUserDirectory(db, clock, external).Resolve(added)).UserId);
        await using (var db = Context())
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).Unlink(new(alice, clock.Now), new(alice, clock.Now)));
        await using (var db = Context())
            await Service(db).Unlink(new(alice, clock.Now), new(added, clock.Now));
        await using (var db = Context())
            Assert.Single(await db.Subjects.Where(s => s.UserId == user.UserId).ToListAsync());
    }

    private static async Task<LifecycleIntent> Intent(IdentityDbContext db, string userId) =>
        JsonSerializer.Deserialize<LifecycleIntent>((await db.Set<OutboxMessage>().Where(m => m.Payload.Contains(userId)).OrderByDescending(m => m.OccurredAt).FirstAsync()).Payload)!;

    private IdentityDbContext Context() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection).Options);

    private SqlAccountLifecycle Service(IdentityDbContext db) => new(db, clock, external, Options.Create(new LifecycleOptions { DeliveryEnabled = true, Consumers = { ["pocketquests"] = "pocketquests-lifecycle" } }));

    private async Task<UserRecord> Seed(ExternalSubject subject)
    {
        await using var db = Context();
        return await new SqlUserDirectory(db, clock, external).Resolve(subject);
    }

    private async Task Process(string userId)
    {
        await using var db = Context();
        await Service(db).Process(userId);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
