namespace HoneyDrunk.Identity.Abstractions.AccountLifecycle;

/// <summary>Versioned, expiring Identity instruction delivered only on the consumer's private transport endpoint.</summary>
/// <param name="UserId">Opaque canonical account ID; never reusable.</param>
/// <param name="Version">Monotonic account lifecycle version.</param>
/// <param name="State">Inactive, Active or Erasing.</param>
/// <param name="EffectiveAt">Authoritative transition time.</param>
/// <param name="ExpiresAt">Reject delivery at or after this instant.</param>
/// <param name="Consumer">Registered destination node.</param>
/// <param name="PausedAt">Original deletion-request instant, including when cancellation arrives before inactivation.</param>
/// <param name="Acknowledgment">Random capability bound to this account, version and consumer.</param>
public sealed record LifecycleIntent(string UserId, long Version, string State, DateTimeOffset EffectiveAt, DateTimeOffset ExpiresAt, string Consumer, string Acknowledgment, DateTimeOffset PausedAt);
