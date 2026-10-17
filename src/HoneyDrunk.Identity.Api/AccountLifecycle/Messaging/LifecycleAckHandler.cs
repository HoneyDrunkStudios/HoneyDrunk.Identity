using HoneyDrunk.Identity.Abstractions.AccountLifecycle;
using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Transport.Abstractions;

namespace HoneyDrunk.Identity.Api.AccountLifecycle.Messaging;

/// <summary>Private receiver; the SQL coordinator also verifies the per-consumer capability.</summary>
public sealed class LifecycleAckHandler(SqlAccountLifecycle lifecycle) : IMessageHandler<LifecycleAck>
{
    /// <inheritdoc />
    public Task HandleAsync(LifecycleAck message, MessageContext context, CancellationToken cancellationToken = default) => lifecycle.Acknowledge(message, cancellationToken);
}
