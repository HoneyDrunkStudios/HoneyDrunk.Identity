using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.Api.Health;

/// <summary>Reports SQL readiness without treating an aborted health probe as an application failure.</summary>
public static class DatabaseHealthEndpoint
{
    /// <summary>Checks database connectivity; cancelled probes are never reported as healthy.</summary>
    /// <param name="db">The scoped Identity database context.</param>
    /// <param name="token">Cancellation for the health request.</param>
    /// <returns>Success only when SQL connectivity was verified.</returns>
    public static async Task<IResult> Check(IdentityDbContext db, CancellationToken token)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(token))
                return Results.StatusCode(503);

            // Connectivity alone accepts an empty database. Zero-row queries also verify
            // the mapped columns and the runtime identity's SELECT permission without reading PII.
            await db.Users.Take(0).ToListAsync(token);
            await db.Subjects.Take(0).ToListAsync(token);
            await db.Audit.Take(0).ToListAsync(token);
            await db.Deliveries.Take(0).ToListAsync(token);
            await db.Erasures.Take(0).ToListAsync(token);
            await db.Set<OutboxMessage>().Take(0).ToListAsync(token);
            return Results.Ok();
        }
        catch (SqlException)
        {
            return Results.StatusCode(503);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Results.StatusCode(503);
        }
    }
}
