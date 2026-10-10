namespace HoneyDrunk.Identity.Persistence.Entities;

/// <summary>The durable, provider-independent account row.</summary>
public sealed class UserEntity
{
    /// <summary>Gets or sets the stable usr-prefixed identifier.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the account lifecycle state.</summary>
    public string State { get; set; } = "Active";

    /// <summary>Gets or sets the first resolution timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the original deletion request instant.</summary>
    public DateTimeOffset? RequestedAt { get; set; }

    /// <summary>Gets or sets the freeze origin retained while cancellation delivery retries.</summary>
    public DateTimeOffset? DeletionPausedAt { get; set; }

    /// <summary>Gets or sets the exclusive recovery deadline.</summary>
    public DateTimeOffset? RecoveryDeadline { get; set; }

    /// <summary>Gets or sets the monotonic lifecycle version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets the transition instant used by downstream pause semantics.</summary>
    public DateTimeOffset? ChangedAt { get; set; }

    /// <summary>Gets or sets a value indicating whether all provider refresh sessions were revoked for this request.</summary>
    public bool SessionsRevoked { get; set; }

    /// <summary>Gets or sets the time for the next bounded worker retry.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Gets or sets a non-content failure code for operational follow-up.</summary>
    public string? FailureCode { get; set; }
}
