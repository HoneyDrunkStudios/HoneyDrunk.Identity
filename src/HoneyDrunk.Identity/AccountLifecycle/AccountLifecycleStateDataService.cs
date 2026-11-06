using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Audit.Data;
using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Abstractions.AccountLifecycle;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Persistence.Entities;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace HoneyDrunk.Identity.AccountLifecycle;

/// <summary>Persists lifecycle state, delivery routes and transactional outbox evidence on the caller's context.</summary>
public sealed class AccountLifecycleStateDataService(IdentityDbContext db, TimeProvider clock, IOptions<LifecycleOptions> options)
{
    /// <summary>Reads one bounded retry batch without resolving provider clients.</summary>
    /// <param name="now">The worker's current time.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>At most twenty account identifiers in retry order.</returns>
    public Task<string[]> PendingUserIds(DateTimeOffset now, CancellationToken token) =>
        db.Users.Where(u => (u.State != "Active" || db.Deliveries.Any(d => d.UserId == u.UserId && !d.Acknowledged)) && (u.NextAttemptAt == null || u.NextAttemptAt <= now))
            .OrderBy(u => u.NextAttemptAt).Select(u => u.UserId).Take(20).ToArrayAsync(token);

    internal async Task<IDbContextTransaction> Lock(CancellationToken token)
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

    internal async Task<List<LifecycleDeliveryEntity>> ReconcileDeliveries(UserEntity user, CancellationToken token)
    {
        var registry = options.Value.Consumers;
        var deliveries = await db.Deliveries.Where(d => d.UserId == user.UserId).ToListAsync(token);
        var retired = deliveries.Where(d => !registry.ContainsKey(d.Consumer)).ToArray();
        db.Deliveries.RemoveRange(retired);
        deliveries.RemoveAll(d => !registry.ContainsKey(d.Consumer));

        var changed = retired.Length > 0;
        foreach (var consumer in registry)
        {
            var delivery = deliveries.SingleOrDefault(d => d.Consumer == consumer.Key);
            if (delivery is null)
            {
                delivery = new LifecycleDeliveryEntity { UserId = user.UserId, Consumer = consumer.Key };
                deliveries.Add(delivery);
                db.Deliveries.Add(delivery);
            }
            else if (delivery.Destination == consumer.Value && delivery.Version == user.Version)
            {
                continue;
            }

            delivery.Destination = consumer.Value;
            delivery.Version = user.Version;
            delivery.Acknowledged = false;
            delivery.ExpiresAt = DateTimeOffset.MinValue;
            changed = true;
        }

        if (changed)
        {
            // Registry changes invalidate pending routes. Renew remaining unacknowledged
            // deliveries atomically with removing old instructions for this account only.
            var ownerProperty = "\"UserId\":" + JsonSerializer.Serialize(user.UserId);
            await db.Set<OutboxMessage>().Where(m => m.Type == typeof(LifecycleIntent).AssemblyQualifiedName && m.Payload.Contains(ownerProperty)).ExecuteDeleteAsync(token);
            foreach (var delivery in deliveries.Where(d => !d.Acknowledged))
                delivery.ExpiresAt = DateTimeOffset.MinValue;
        }

        return deliveries;
    }

    internal async Task<List<LifecycleDeliveryEntity>> Transition(UserEntity user, string state, CancellationToken token)
    {
        user.State = state;
        user.Version++;
        user.ChangedAt = clock.GetUtcNow();
        user.NextAttemptAt = null;
        user.FailureCode = null;
        var deliveries = await ReconcileDeliveries(user, token);
        foreach (var delivery in deliveries)
        {
            delivery.Version = user.Version;
            delivery.Acknowledged = false;
            Queue(user, delivery);
        }

        return deliveries;
    }

    internal void Queue(UserEntity user, LifecycleDeliveryEntity delivery)
    {
        delivery.Acknowledgment = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        delivery.ExpiresAt = clock.GetUtcNow().AddHours(1);
        var intent = new LifecycleIntent(user.UserId, user.Version, user.State, user.ChangedAt!.Value, delivery.ExpiresAt, delivery.Consumer, delivery.Acknowledgment, user.DeletionPausedAt ?? user.ChangedAt.Value);
        db.Set<OutboxMessage>().Add(new()
        {
            Id = Guid.NewGuid(),
            Type = typeof(LifecycleIntent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(intent),
            OccurredAt = clock.GetUtcNow(),
            TenantId = "internal",
            CorrelationId = Guid.NewGuid().ToString("N"),
            Headers = JsonSerializer.Serialize(new Dictionary<string, string> { [OutboxHeaderNames.Destination] = delivery.Destination }),
        });
    }

    internal void Audit(UserEntity user, string name) => db.Audit.Add(AuditRecord.FromEntry(new AuditEntry(AuditEntryId.New(), clock.GetUtcNow(), user.UserId, name, AuditCategory.Security, AuditOutcome.Succeeded, new AuditTarget("user", user.UserId), TenantId.Internal)));
}
