namespace HoneyDrunk.Identity.Abstractions;

/// <summary>The provider-independent account returned to consuming applications.</summary>
/// <param name="UserId">Stable HoneyDrunk user identifier.</param>
/// <param name="State">Account lifecycle state; consumers require Active.</param>
/// <param name="CreatedAt">Server timestamp of the first successful resolution.</param>
public sealed record UserRecord(string UserId, string State, DateTimeOffset CreatedAt);
