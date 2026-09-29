namespace HoneyDrunk.Identity.Persistence.Entities;

/// <summary>Durable account lifecycle coordination state.</summary>
public sealed class LifecycleDeliveryEntity
{
    /// <summary>Gets or sets canonical account.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets registered consumer.</summary>
    public string Consumer { get; set; } = string.Empty;

    /// <summary>Gets or sets private queue destination captured at request time.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Gets or sets monotonic version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets random per-consumer acknowledgment capability.</summary>
    public string Acknowledgment { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the consumer acknowledged this version.</summary>
    public bool Acknowledged { get; set; }

    /// <summary>Gets or sets message validity deadline.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
