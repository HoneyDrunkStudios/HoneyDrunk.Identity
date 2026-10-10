using HoneyDrunk.Identity.Api.Health;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.Tests.Health;

/// <summary>Checks readiness against SQL and cancelled startup probes.</summary>
public sealed class DatabaseHealthTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    /// <summary>A cancelled SQL probe must return unavailable without escaping into the debugger.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task CancelledProbeReturnsUnavailable()
    {
        await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(sql.NewDatabaseConnection()).Options);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await DatabaseHealthEndpoint.Check(db, cancellation.Token);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>Readiness distinguishes an absent database from a migrated database.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task ReadinessRequiresAnAvailableDatabase()
    {
        await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(sql.NewDatabaseConnection()).Options);
        try
        {
            var absent = await DatabaseHealthEndpoint.Check(db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(absent).StatusCode);

            await DatabaseSchema.DeployAsync(db.Database);
            var ready = await DatabaseHealthEndpoint.Check(db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(ready).StatusCode);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
