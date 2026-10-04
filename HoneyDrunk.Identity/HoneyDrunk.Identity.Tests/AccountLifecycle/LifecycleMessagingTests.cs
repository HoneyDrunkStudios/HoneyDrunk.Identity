using Azure.Messaging.ServiceBus;
using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Data.Outbox.Dispatcher;
using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Identity.Api.AccountLifecycle;
using HoneyDrunk.Identity.Api.AccountLifecycle.Messaging;
using HoneyDrunk.Transport.Abstractions;
using HoneyDrunk.Transport.AzureServiceBus.Configuration;
using HoneyDrunk.Transport.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HoneyDrunk.Identity.Tests.AccountLifecycle;

/// <summary>Lifecycle messaging composition tests with in-process broker and outbox fakes.</summary>
public sealed class LifecycleMessagingTests
{
    private const string Queue = "synthetic-lifecycle";

    /// <summary>The lifecycle host requires manual settlement and broker-only publishing.</summary>
    [Fact]
    public void AddLifecycleRuntime_Configured_RequiresManualSettlementWithoutFallback()
    {
        var builder = CreateBuilder();
        builder.AddLifecycleRuntime();
        using var services = builder.Services.BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;

        Assert.False(options.AutoComplete);
        Assert.False(options.BlobFallback.Enabled);
    }

    /// <summary>Later configuration cannot silently weaken lifecycle delivery guarantees.</summary>
    /// <param name="autoComplete">Whether to enable unsafe automatic completion instead of fallback.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddLifecycleRuntime_UnsafeOverride_RejectsOptions(bool autoComplete)
    {
        var builder = CreateBuilder();
        builder.AddLifecycleRuntime();
        builder.Services.PostConfigure<AzureServiceBusOptions>(options =>
        {
            options.AutoComplete = autoComplete;
            options.BlobFallback.Enabled = !autoComplete;
        });
        using var services = builder.Services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value);
    }

    /// <summary>A failed lifecycle broker send reaches the caller in single and batch publishing.</summary>
    /// <param name="batch">Whether the batch entry point is used.</param>
    /// <returns>The publisher boundary test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishAsync_BrokerFailure_PropagatesOriginalFailure(bool batch)
    {
        await using var client = new TestBusClient(fail: true);
        await using var publisher = CreatePublisher(client);
        var envelope = new EnvelopeFactory(TimeProvider.System).CreateEnvelope<object>(BinaryData.FromString("{}").ToMemory());
        var destination = EndpointAddress.Create(Queue, Queue);

        var observed = await Record.ExceptionAsync(() => batch
            ? publisher.PublishBatchAsync([envelope], destination)
            : publisher.PublishAsync(envelope, destination));

        Assert.Same(client.Sender.Failure, observed);
        Assert.Equal(1, client.Sender.Attempts);
    }

    /// <summary>The real Data dispatcher retries unsent lifecycle messages and marks only successful sends.</summary>
    /// <param name="fail">Whether the synthetic broker rejects the send.</param>
    /// <returns>The dispatch composition test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DispatchPendingAsync_BrokerOutcome_PreservesOutboxDisposition(bool fail)
    {
        await using var client = new TestBusClient(fail);
        await using var publisher = CreatePublisher(client);
        var reader = new RecordingOutboxReader();
        await using var services = new ServiceCollection().AddSingleton<IOutboxReader>(reader)
            .AddSingleton<ITransportPublisher>(publisher).BuildServiceProvider();
        using var dispatcher = new OutboxDispatcherService(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new OutboxDispatcherOptions { DefaultDestination = Queue }), NullLogger<OutboxDispatcherService>.Instance);

        await dispatcher.DispatchPendingAsync();

        Assert.Equal(fail ? OutboxMessageStatus.Pending : OutboxMessageStatus.Dispatched, reader.Status);
        Assert.Equal(fail ? 0 : 1, reader.DispatchedCount);
        Assert.Equal(fail ? 1 : 0, reader.RetryCount);
        Assert.Equal(1, client.Sender.Attempts);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lifecycle:ServiceBusNamespace"] = "synthetic.invalid",
            ["Lifecycle:ConsumerQueue"] = Queue,
            ["Lifecycle:AcknowledgmentQueue"] = Queue,
        });
        return builder;
    }

    private static RegisteredLifecyclePublisher CreatePublisher(ServiceBusClient client) =>
        new(client, Options.Create(new LifecycleOptions { Consumers = { ["pocketquests"] = Queue } }), NullLoggerFactory.Instance, TimeProvider.System);

    private sealed class TestBusClient(bool fail) : ServiceBusClient
    {
        public TestBusSender Sender { get; } = new(fail);

        public override ServiceBusSender CreateSender(string queueOrTopicName) =>
            queueOrTopicName == Queue ? Sender : throw new InvalidOperationException("Unexpected test destination.");

        // The SDK mock constructor has no live connection to dispose.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2215:Dispose methods should call base class dispose", Justification = "The protected ServiceBusClient mocking constructor leaves connection state unset; base disposal throws and owns no resources.")]
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestBusSender(bool fail) : ServiceBusSender
    {
        public InvalidOperationException Failure { get; } = new("Synthetic broker send failure.");

        public int Attempts { get; private set; }

        // The SDK mock constructor has no live sender to close.
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return fail ? Task.FromException(Failure) : Task.CompletedTask;
        }
    }

    // Product-specific composition seam, not a second persistence implementation.
    private sealed class RecordingOutboxReader : IOutboxReader
    {
        private readonly OutboxMessage message = new()
        {
            Id = Guid.NewGuid(), Type = "Synthetic.Lifecycle", Payload = "{}", OccurredAt = DateTimeOffset.UtcNow,
        };

        public OutboxMessageStatus Status { get; private set; } = OutboxMessageStatus.Pending;

        public int DispatchedCount { get; private set; }

        public int RetryCount { get; private set; }

        public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            Status = OutboxMessageStatus.Leased;
            return Task.FromResult<IReadOnlyList<OutboxMessage>>([message]);
        }

        public Task MarkDispatchedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(message.Id, messageId);
            Status = OutboxMessageStatus.Dispatched;
            DispatchedCount++;
            return Task.CompletedTask;
        }

        public Task ReleaseForRetryAsync(Guid messageId, int retryCount, DateTimeOffset nextAttemptAt, string? lastError = null, CancellationToken cancellationToken = default)
        {
            Assert.Equal(message.Id, messageId);
            Status = OutboxMessageStatus.Pending;
            RetryCount = retryCount;
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(Guid messageId, string? lastError = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A first transient failure must be retried.");

        public Task CleanupDispatchedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Cleanup is outside this dispatch test.");
    }
}
