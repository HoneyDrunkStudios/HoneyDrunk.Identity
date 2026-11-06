using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.Health;

/// <summary>Checks connectivity and mapped schema access without reading account data.</summary>
/// <param name="db">The scoped Identity database context.</param>
public sealed class IdentityDatabaseHealthDataService(IdentityDbContext db)
{
    /// <summary>Verifies connectivity and zero-row access to every mapped lifecycle table.</summary>
    /// <param name="token">Cancellation.</param>
    /// <returns>Whether connectivity and schema access succeeded.</returns>
    public async Task<bool> IsReady(CancellationToken token)
    {
        if (!await db.Database.CanConnectAsync(token))
            return false;

        // Zero-row queries validate mapped columns and SELECT permission without reading PII.
        await db.Users.Take(0).ToListAsync(token);
        await db.Subjects.Take(0).ToListAsync(token);
        await db.Audit.Take(0).ToListAsync(token);
        await db.Deliveries.Take(0).ToListAsync(token);
        await db.Erasures.Take(0).ToListAsync(token);
        await db.Set<OutboxMessage>().Take(0).ToListAsync(token);
        return true;
    }
}
