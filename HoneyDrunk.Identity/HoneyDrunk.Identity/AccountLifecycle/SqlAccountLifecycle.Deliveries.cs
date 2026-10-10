using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Abstractions.AccountLifecycle;
using HoneyDrunk.Identity.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HoneyDrunk.Identity.AccountLifecycle;

/// <summary>Reconciles durable delivery routes under the account lifecycle transaction lock.</summary>
public sealed partial class SqlAccountLifecycle
{
    private async Task<List<LifecycleDeliveryEntity>> ReconcileDeliveries(UserEntity user, CancellationToken token)
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
}
