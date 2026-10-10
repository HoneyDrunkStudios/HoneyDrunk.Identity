using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Transport.Abstractions;
using HoneyDrunk.Transport.AzureServiceBus;
using HoneyDrunk.Transport.AzureServiceBus.Configuration;
using Microsoft.Extensions.Options;

namespace HoneyDrunk.Identity.Api.AccountLifecycle.Messaging;

/// <summary>Uses a separately configured published Transport publisher for each registered queue.</summary>
public sealed class RegisteredLifecyclePublisher : ITransportPublisher, IAsyncDisposable
{
    private readonly Dictionary<string, ServiceBusTransportPublisher> publishers;
    private readonly TimeProvider clock;

    /// <summary>Initializes a new instance of the <see cref="RegisteredLifecyclePublisher"/> class.</summary>
    /// <param name="client">Shared managed-identity broker client.</param>
    /// <param name="registry">Environment-owned destination registry.</param>
    /// <param name="logger">Publisher logging.</param>
    /// <param name="clock">Authoritative time.</param>
    public RegisteredLifecyclePublisher(Azure.Messaging.ServiceBus.ServiceBusClient client, IOptions<LifecycleOptions> registry, ILoggerFactory logger, TimeProvider clock)
    {
        this.clock = clock;

        // A failed broker send must remain retryable in SQL; Blob persistence is not delivery.
        publishers = registry.Value.Consumers.Values.Distinct(StringComparer.Ordinal).ToDictionary(address => address, address => new ServiceBusTransportPublisher(client, Options.Create(new AzureServiceBusOptions { Address = address, BlobFallback = new() { Enabled = false } }), logger.CreateLogger<ServiceBusTransportPublisher>()), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task PublishAsync(ITransportEnvelope envelope, IEndpointAddress destination, CancellationToken cancellationToken = default)
    {
        if (!publishers.TryGetValue(destination.Address, out var publisher))
            throw new InvalidOperationException("Unregistered lifecycle destination.");
        var remaining = envelope.Timestamp.AddHours(1) - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            throw new InvalidOperationException("Lifecycle instruction expired; the coordinator must renew it.");
        return publisher.PublishAsync(envelope, EndpointAddress.Create(destination.Name, destination.Address, timeToLive: remaining), cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishBatchAsync(IEnumerable<ITransportEnvelope> envelopes, IEndpointAddress destination, CancellationToken cancellationToken = default)
    {
        foreach (var envelope in envelopes)
            await PublishAsync(envelope, destination, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var publisher in publishers.Values)
            await publisher.DisposeAsync();
    }
}
