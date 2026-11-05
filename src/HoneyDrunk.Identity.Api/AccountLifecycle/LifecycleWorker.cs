using HoneyDrunk.Identity.AccountLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
                if (query.ServiceProvider.GetRequiredService<IOptions<LifecycleOptions>>().Value.DeliveryEnabled)
                {
                    var pending = await query.ServiceProvider.GetRequiredService<SqlAccountLifecycleUnitOfWork>().Pending(stoppingToken);
                    foreach (var id in pending)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<SqlAccountLifecycleUnitOfWork>().Process(id, stoppingToken);
                    }
                }

                await query.ServiceProvider.GetRequiredService<SqlAccountLifecycleUnitOfWork>().Prune(stoppingToken);
            }
            catch (Exception error) when (error is System.Data.Common.DbException or DbUpdateException or InvalidOperationException or TimeoutException)
            {
                LifecycleFailureLog.Write(logger, error);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
