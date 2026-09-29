using HoneyDrunk.Data.Outbox.Dispatcher.Registration;
using HoneyDrunk.Data.Outbox.Registration;
using HoneyDrunk.Identity.Abstractions.AccountLifecycle;
using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Identity.Api.AccountLifecycle.Messaging;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Transport.Abstractions;
using HoneyDrunk.Transport.AzureServiceBus.DependencyInjection;
using HoneyDrunk.Transport.DependencyInjection;

namespace HoneyDrunk.Identity.Api.AccountLifecycle;

/// <summary>Opt-in private Transport delivery and bounded lifecycle maintenance.</summary>
public static class LifecycleRuntime
{
    /// <summary>Composes existing Data outbox and Transport nodes only when the environment is configured.</summary>
    /// <param name="builder">Identity application builder.</param>
    public static void AddLifecycleRuntime(this WebApplicationBuilder builder)
    {
        var bus = builder.Configuration["Lifecycle:ServiceBusNamespace"];
        builder.Services.PostConfigure<LifecycleOptions>(options => options.DeliveryEnabled = !string.IsNullOrWhiteSpace(bus));
        if (string.IsNullOrWhiteSpace(bus))
            return;
        var queue = builder.Configuration["Lifecycle:AcknowledgmentQueue"] ?? throw new InvalidOperationException("Private acknowledgment queue is required.");
        builder.Services.AddHoneyDrunkDataOutbox<IdentityDbContext>();
        builder.Services.AddHoneyDrunkServiceBusTransportWithManagedIdentity(bus, queue);
        builder.Services.AddSingleton<ITransportPublisher, RegisteredLifecyclePublisher>();
        builder.Services.AddMessageHandler<LifecycleAck, LifecycleAckHandler>();
        builder.Services.AddOutboxDispatcher();
        builder.Services.AddHostedService<LifecycleWorker>();
    }
}
