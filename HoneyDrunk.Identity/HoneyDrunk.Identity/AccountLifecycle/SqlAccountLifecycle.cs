using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Audit.Data;
using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Abstractions.AccountLifecycle;
using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.Accounts;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Persistence.Entities;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace HoneyDrunk.Identity.AccountLifecycle;

/// <summary>Serializes verified account lifecycle transitions and their transactional delivery intents.</summary>
public sealed partial class SqlAccountLifecycle(IdentityDbContext db, TimeProvider clock, IExternalAccounts external, IOptions<LifecycleOptions> options)
{
    /// <summary>Reads an existing account without restoring ordinary access.</summary>
    /// <param name="subject">Validated owner identity.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The account status.</returns>
    public async Task<AccountStatus> Status(ExternalSubject subject, CancellationToken token = default)
    {
        await using var tx = await Lock(token);
        return View(await Owner(subject, token));
    }

    /// <summary>Inactivates the owner immediately; repeats preserve the original recovery deadline.</summary>
    /// <param name="login">Recent verified authentication.</param>
    /// <param name="confirmed">Explicit deletion confirmation.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The durable deletion status.</returns>
    public async Task<AccountStatus> Request(VerifiedLogin login, bool confirmed, CancellationToken token = default)
    {
        Fresh(login);
        if (!confirmed)
            throw new ArgumentException("Explicit deletion confirmation is required.");
        await using var tx = await Lock(token);
        var user = await Owner(login.Subject, token);
        if (user.State == "Active")
        {
            if (!options.Value.DeliveryEnabled || options.Value.Consumers.Count == 0)
                throw new InvalidOperationException("Deletion consumer registry is not configured.");
            user.RequestedAt = clock.GetUtcNow();
            user.DeletionPausedAt = user.RequestedAt;
            user.RecoveryDeadline = user.RequestedAt.Value.AddDays(30);
            user.SessionsRevoked = false;
            await Transition(user, "Inactive", token);
            Audit(user, "identity.deletion.requested");
            await db.SaveChangesAsync(token);
        }

        await tx.CommitAsync(token);
        return View(user);
    }

    /// <summary>Explicitly cancels strictly before the original deadline; downstream commitments remain paused.</summary>
    /// <param name="login">Recent same-account authentication.</param>
    /// <param name="confirmed">Explicit recovery confirmation.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The restored account status.</returns>
    public async Task<AccountStatus> Cancel(VerifiedLogin login, bool confirmed, CancellationToken token = default)
    {
        Fresh(login);
        if (!confirmed)
            throw new ArgumentException("Explicit recovery confirmation is required.");
        await using var tx = await Lock(token);
        var user = await Owner(login.Subject, token);
        if (user.State != "Active")
        {
            if (user.State != "Inactive" || clock.GetUtcNow() >= user.RecoveryDeadline)
                throw new InvalidOperationException("The recovery window has ended; erasure continues.");
            await Transition(user, "Active", token);
            user.RequestedAt = null;
            user.RecoveryDeadline = null;
            Audit(user, "identity.deletion.cancelled");
            await db.SaveChangesAsync(token);
        }

        await tx.CommitAsync(token);
        return View(user);
    }

    /// <summary>Accepts only the current registered consumer's capability after its committed mutation.</summary>
    /// <param name="ack">Receipt received on Identity's private Transport endpoint.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The durable receipt write.</returns>
    public async Task Acknowledge(LifecycleAck ack, CancellationToken token = default)
    {
        await using var tx = await Lock(token);
        var row = await db.Deliveries.SingleOrDefaultAsync(d => d.UserId == ack.UserId && d.Consumer == ack.Consumer, token);
        if (row is null || row.Version != ack.Version || row.ExpiresAt <= clock.GetUtcNow())
            return;
        if (ack.Acknowledgment.Length != 64 || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(row.Acknowledgment), System.Text.Encoding.ASCII.GetBytes(ack.Acknowledgment)))
            throw new UnauthorizedAccessException("Invalid lifecycle acknowledgment.");
        row.Acknowledged = true;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }

    private static AccountStatus View(UserEntity user) => new(user.UserId, user.State, user.RequestedAt, user.RecoveryDeadline, user.Version);

    private async Task<IDbContextTransaction> Lock(CancellationToken token)
    {
        var tx = await db.Database.BeginTransactionAsync(token);
        try
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='identity:lifecycle',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r < 0 THROW 51000, 'Identity transaction busy.', 1;", token);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private async Task<UserEntity> Owner(ExternalSubject subject, CancellationToken token)
    {
        var key = SqlUserDirectory.SubjectKey(subject);
        var mapping = await db.Subjects.SingleOrDefaultAsync(s => s.SubjectKey == key, token)
            ?? throw new UnauthorizedAccessException("Account does not exist.");
        if (mapping.ObjectId is not null && mapping.ObjectId != subject.ObjectId)
            throw new UnauthorizedAccessException("Provider account binding changed.");
        return await db.Users.SingleAsync(u => u.UserId == mapping.UserId, token);
    }

    private void Fresh(VerifiedLogin login)
    {
        var now = clock.GetUtcNow();
        if (login.AuthenticatedAt is null || login.AuthenticatedAt > now.AddSeconds(30) || login.AuthenticatedAt <= now.AddMinutes(-5))
            throw new UnauthorizedAccessException("Sign in again to confirm this account change.");
    }

    private async Task Transition(UserEntity user, string state, CancellationToken token)
    {
        user.State = state;
        user.Version++;
        user.ChangedAt = clock.GetUtcNow();
        user.NextAttemptAt = null;
        user.FailureCode = null;
        var deliveries = await db.Deliveries.Where(d => d.UserId == user.UserId).ToListAsync(token);
        foreach (var consumer in options.Value.Consumers)
        {
            if (deliveries.All(d => d.Consumer != consumer.Key))
            {
                var added = new LifecycleDeliveryEntity { UserId = user.UserId, Consumer = consumer.Key, Destination = consumer.Value };
                deliveries.Add(added);
                db.Deliveries.Add(added);
            }
        }

        foreach (var delivery in deliveries)
        {
            delivery.Version = user.Version;
            delivery.Acknowledged = false;
            Queue(user, delivery);
        }
    }

    private void Queue(UserEntity user, LifecycleDeliveryEntity delivery)
    {
        delivery.Acknowledgment = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        delivery.ExpiresAt = clock.GetUtcNow().AddHours(1);
        var intent = new LifecycleIntent(user.UserId, user.Version, user.State, user.ChangedAt!.Value, delivery.ExpiresAt, delivery.Consumer, delivery.Acknowledgment, user.DeletionPausedAt ?? user.ChangedAt!.Value);
        db.Set<OutboxMessage>().Add(new()
        {
            Id = Guid.NewGuid(), Type = typeof(LifecycleIntent).AssemblyQualifiedName!, Payload = JsonSerializer.Serialize(intent),
            OccurredAt = clock.GetUtcNow(), TenantId = "internal", CorrelationId = Guid.NewGuid().ToString("N"),
            Headers = JsonSerializer.Serialize(new Dictionary<string, string> { [OutboxHeaderNames.Destination] = delivery.Destination }),
        });
    }

    private void Audit(UserEntity user, string name) => db.Audit.Add(AuditRecord.FromEntry(new AuditEntry(AuditEntryId.New(), clock.GetUtcNow(), user.UserId, name, AuditCategory.Security, AuditOutcome.Succeeded, new AuditTarget("user", user.UserId), TenantId.Internal)));
}
