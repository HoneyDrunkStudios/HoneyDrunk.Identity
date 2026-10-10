namespace HoneyDrunk.Identity.Abstractions.Accounts;

/// <summary>The provider-independent account returned to consuming applications.</summary>
/// <param name="UserId">Stable HoneyDrunk user identifier.</param>
/// <param name="State">Account lifecycle state; consumers require Active.</param>
/// <param name="CreatedAt">Server timestamp of the first successful resolution.</param>
/// <param name="LifecycleVersion">Monotonic version allowing consumers to fence delayed lifecycle delivery.</param>
/// <param name="DeletionPausedAt">Freeze origin retained after recovery until explicit product resume.</param>
public sealed record UserRecord(string UserId, string State, DateTimeOffset CreatedAt, long LifecycleVersion = 0, DateTimeOffset? DeletionPausedAt = null);
