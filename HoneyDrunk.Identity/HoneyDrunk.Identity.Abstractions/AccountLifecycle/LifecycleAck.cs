namespace HoneyDrunk.Identity.Abstractions.AccountLifecycle;

/// <summary>Receipt emitted only after the consumer commits the lifecycle mutation.</summary>
/// <param name="UserId">Canonical account ID.</param>
/// <param name="Version">Acknowledged lifecycle version.</param>
/// <param name="Consumer">Registered consumer.</param>
/// <param name="Acknowledgment">Capability copied from the accepted instruction.</param>
public sealed record LifecycleAck(string UserId, long Version, string Consumer, string Acknowledgment);
