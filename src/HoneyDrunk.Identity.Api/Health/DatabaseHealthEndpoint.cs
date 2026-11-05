using HoneyDrunk.Identity.Persistence.Health;
using Microsoft.Data.SqlClient;

namespace HoneyDrunk.Identity.Api.Health;

/// <summary>Reports SQL readiness without treating an aborted health probe as an application failure.</summary>
public static class DatabaseHealthEndpoint
{
    /// <summary>Checks database connectivity; cancelled probes are never reported as healthy.</summary>
    /// <param name="readiness">The scoped database readiness data service.</param>
    /// <param name="token">Cancellation for the health request.</param>
    /// <returns>Success only when SQL connectivity was verified.</returns>
    public static async Task<IResult> Check(IdentityReadinessDataService readiness, CancellationToken token)
    {
        try
        {
            return await readiness.Check(token) ? Results.Ok() : Results.StatusCode(503);
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
