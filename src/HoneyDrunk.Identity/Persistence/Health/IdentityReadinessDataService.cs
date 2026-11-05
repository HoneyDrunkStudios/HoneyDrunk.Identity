using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.Persistence.Health;

/// <summary>Verifies SQL connectivity, mapped columns and runtime read permissions without reading account data.</summary>
/// <param name="db">The scoped Identity database context.</param>
public sealed class IdentityReadinessDataService(IdentityDbContext db)
{
    /// <summary>Checks the schema used by the running Identity service.</summary>
    /// <param name="token">Cancellation for the readiness probe.</param>
    /// <returns>Whether SQL connectivity and zero-row schema queries succeeded.</returns>
    public async Task<bool> Check(CancellationToken token)
    {
        if (!await db.Database.CanConnectAsync(token))
            return false;

        await db.Users.Take(0).ToListAsync(token);
        await db.Subjects.Take(0).ToListAsync(token);
        await db.Audit.Take(0).ToListAsync(token);
        await db.Deliveries.Take(0).ToListAsync(token);
        await db.Erasures.Take(0).ToListAsync(token);
        await db.Set<OutboxMessage>().Take(0).ToListAsync(token);
        return true;
    }
}
