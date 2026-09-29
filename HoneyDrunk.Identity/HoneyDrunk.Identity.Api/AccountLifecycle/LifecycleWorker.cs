using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Identity.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.Api.AccountLifecycle;

/// <summary>Periodic bounded retry; failed accounts remain inaccessible and expose a durable failure code.</summary>
public sealed class LifecycleWorker(IServiceScopeFactory scopes, ILogger<LifecycleWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var query = scopes.CreateAsyncScope();
                var db = query.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var now = query.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
                var pending = await db.Users.Where(u => (u.State != "Active" || db.Deliveries.Any(d => d.UserId == u.UserId && !d.Acknowledged)) && (u.NextAttemptAt == null || u.NextAttemptAt <= now))
                    .OrderBy(u => u.NextAttemptAt).Select(u => u.UserId).Take(20).ToArrayAsync(stoppingToken);
                foreach (var id in pending)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SqlAccountLifecycle>().Process(id, stoppingToken);
                }

                await query.ServiceProvider.GetRequiredService<SqlAccountLifecycle>().Prune(stoppingToken);
            }
            catch (Exception error) when (error is System.Data.Common.DbException or InvalidOperationException or TimeoutException)
            {
                logger.LogError("Lifecycle maintenance failed ({FailureType}); access restrictions remain in force.", error.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
