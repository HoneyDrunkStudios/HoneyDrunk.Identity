using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Audit.Data;
using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Identity.Accounts;
using HoneyDrunk.Identity.Api.AccountLifecycle;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Tests.Fixtures;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoneyDrunk.Identity.Tests.AccountLifecycle;

/// <summary>Real-SQL hosted maintenance retries and transport-independent retention boundaries.</summary>
public sealed class LifecycleMaintenanceTests(SqlServerFixture sql) : IAsyncLifetime, IClassFixture<SqlServerFixture>
{
    private readonly string connection = sql.NewDatabaseConnection();
    private readonly MaintenanceClock clock = new();
    private readonly ExternalAccounts external = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await using var db = Context();
        await DatabaseSchema.DeployAsync(db.Database);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await using var db = Context();
        await db.Database.EnsureDeletedAsync();
    }

    /// <summary>An EF write failure rolls back, leaves the real host running, and is retried by the next maintenance cycle.</summary>
    /// <returns>The hosted SQL regression.</returns>
    [Fact]
    public async Task EfWriteFailureKeepsHostRunningAndRetries()
    {
        var (user, _) = await Seed("pending", requestDeletion: true);
        var failure = new FailFirstSave();
        var logger = new MaintenanceLogger();
        var builder = Builder(failure);
        builder.Services.Configure<LifecycleOptions>(options => options.DeliveryEnabled = true);
        builder.Services.AddSingleton<ILogger<LifecycleWorker>>(logger);
        builder.Services.AddHostedService<LifecycleWorker>();
        await using var host = builder.Build();
        var worker = Assert.Single(host.Services.GetServices<IHostedService>().OfType<LifecycleWorker>());
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.Equal(BackgroundServiceExceptionBehavior.StopHost, host.Services.GetRequiredService<IOptions<HostOptions>>().Value.BackgroundServiceExceptionBehavior);
        await host.StartAsync();
        try
        {
            await logger.Failure.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
            Assert.False(worker.ExecuteTask!.IsCompleted);
            await using (var db = Context())
            {
                var unchanged = await db.Users.SingleAsync(u => u.UserId == user.UserId);
                Assert.Equal("Inactive", unchanged.State);
                Assert.False(unchanged.SessionsRevoked);
                Assert.Null(unchanged.NextAttemptAt);
                Assert.Single(await db.Set<OutboxMessage>().ToListAsync());
            }

            await failure.Saved.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await WaitFor(async () =>
            {
                await using var db = Context();
                return await db.Users.AnyAsync(u => u.UserId == user.UserId && u.SessionsRevoked && u.NextAttemptAt != null);
            });
            Assert.Equal(2, failure.Attempts);
            Assert.Equal(2, external.Revocations);
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
            Assert.False(worker.ExecuteTask.IsCompleted);
        }
        finally
        {
            await Stop(host);
        }

        Assert.False(worker.ExecuteTask!.IsFaulted);
    }

    /// <summary>Missing messaging still schedules retention while requests and existing lifecycle delivery stay disabled.</summary>
    /// <returns>The hosted SQL regression.</returns>
    [Fact]
    public async Task MessagingDisabledStillPrunesExpiredRecordsWithoutProcessingAccounts()
    {
        var (pending, _) = await Seed("pending", requestDeletion: true);
        var (active, subject) = await Seed("active", requestDeletion: false);
        await using (var db = Context())
        {
            db.Erasures.AddRange(new() { UserId = "usr_expired", ErasedAt = clock.Now.AddDays(-35) }, new() { UserId = "usr_retained", ErasedAt = clock.Now.AddDays(-34) });
            db.Audit.AddRange(Validation("anonymous", clock.Now.AddDays(-30)), Validation("anonymous", clock.Now.AddDays(-29)), Validation(active.UserId, clock.Now.AddDays(-40)));
            await db.SaveChangesAsync();
        }

        var builder = Builder();
        builder.AddLifecycleRuntime();
        await using var host = builder.Build();
        var worker = Assert.Single(host.Services.GetServices<IHostedService>().OfType<LifecycleWorker>());
        Assert.False(host.Services.GetRequiredService<IOptions<LifecycleOptions>>().Value.DeliveryEnabled);
        await host.StartAsync();
        try
        {
            await WaitFor(async () =>
            {
                await using var db = Context();
                return !await db.Erasures.AnyAsync(m => m.UserId == "usr_expired");
            });
            await using (var db = Context())
            {
                Assert.Equal("usr_retained", (await db.Erasures.SingleAsync()).UserId);
                Assert.Single(await db.Audit.Where(a => a.Actor == "anonymous").ToListAsync());
                Assert.Single(await db.Audit.Where(a => a.Actor == active.UserId && a.EventName == "auth.token.validate").ToListAsync());
                Assert.Single(await db.Set<OutboxMessage>().ToListAsync());
                Assert.False((await db.Users.SingleAsync(u => u.UserId == pending.UserId)).SessionsRevoked);
            }

            await using (var scope = host.Services.CreateAsyncScope())
                await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<SqlAccountLifecycle>().Request(new(subject, clock.Now), true));

            clock.Now = clock.Now.AddDays(1);
            await WaitFor(async () =>
            {
                await using var db = Context();
                return !await db.Erasures.AnyAsync();
            });
            await using (var db = Context())
            {
                Assert.Empty(await db.Audit.Where(a => a.Actor == "anonymous").ToListAsync());
                Assert.Single(await db.Audit.Where(a => a.Actor == active.UserId && a.EventName == "auth.token.validate").ToListAsync());
                Assert.Equal("Active", (await db.Users.SingleAsync(u => u.UserId == active.UserId)).State);
                var unchanged = await db.Users.SingleAsync(u => u.UserId == pending.UserId);
                Assert.Equal("Inactive", unchanged.State);
                Assert.False(unchanged.SessionsRevoked);
                Assert.Null(unchanged.NextAttemptAt);
            }

            Assert.Equal(0, external.Revocations);
            Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        }
        finally
        {
            await Stop(host);
        }

        Assert.False(worker.ExecuteTask!.IsFaulted);
    }

    private static AuditRecord Validation(string actor, DateTimeOffset at) => AuditRecord.FromEntry(new AuditEntry(AuditEntryId.New(), at, actor, "auth.token.validate", AuditCategory.Security, AuditOutcome.Succeeded, new AuditTarget("identity.validation", actor), TenantId.Internal));

    private static async Task WaitFor(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!await predicate())
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
    }

    private static async Task Stop(WebApplication host)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StopAsync(timeout.Token);
    }

    private IdentityDbContext Context() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection).Options);

    private WebApplicationBuilder Builder(SaveChangesInterceptor? interceptor = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IExternalAccounts>(external);
        builder.Services.AddDbContext<IdentityDbContext>(options =>
        {
            options.UseSqlServer(connection);
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
        });
        builder.Services.AddScoped<SqlAccountLifecycle>();
        builder.Services.Configure<LifecycleOptions>(options => options.Consumers["pocketquests"] = "test-only-private-queue");
        return builder;
    }

    private async Task<(UserRecord user, ExternalSubject subject)> Seed(string name, bool requestDeletion)
    {
        var subject = new ExternalSubject("https://identity.test", name, Guid.NewGuid().ToString());
        await using var db = Context();
        var user = await new SqlUserDirectory(db, clock, external).Resolve(subject);
        if (requestDeletion)
            await new SqlAccountLifecycle(db, clock, external, Options.Create(new LifecycleOptions { DeliveryEnabled = true, Consumers = { ["pocketquests"] = "test-only-private-queue" } })).Request(new(subject, clock.Now), true);
        return (user, subject);
    }

    private sealed class MaintenanceClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExternalAccounts : IExternalAccounts
    {
        private int revocations;

        public int Revocations => Volatile.Read(ref revocations);

        public Task<bool> Exists(ExternalSubject subject, CancellationToken token) => Task.FromResult(true);

        public Task Revoke(ExternalSubject subject, CancellationToken token)
        {
            Interlocked.Increment(ref revocations);
            return Task.CompletedTask;
        }

        public Task Erase(ExternalSubject subject, CancellationToken token) => throw new InvalidOperationException("This test must not erase an account.");
    }

    private sealed class FailFirstSave : SaveChangesInterceptor
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new DbUpdateException("Injected maintenance write failure.", new TimeoutException("Injected timeout; no live SQL timeout is claimed."));
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Saved.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class MaintenanceLogger : ILogger<LifecycleWorker>
    {
        public TaskCompletionSource Failure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Failure.TrySetResult();
        }
    }
}
