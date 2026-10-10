namespace HoneyDrunk.Identity.Abstractions.Accounts;

/// <summary>Account lifecycle state exposed to its verified owner even while ordinary access is inactive.</summary>
/// <param name="UserId">Stable account ID.</param>
/// <param name="State">Active, Inactive or Erasing.</param>
/// <param name="RequestedAt">Deletion request time.</param>
/// <param name="RecoveryDeadline">Exclusive recovery deadline.</param>
/// <param name="Version">Monotonic lifecycle version.</param>
public sealed record AccountStatus(string UserId, string State, DateTimeOffset? RequestedAt, DateTimeOffset? RecoveryDeadline, long Version);
