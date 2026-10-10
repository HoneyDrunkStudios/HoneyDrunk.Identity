namespace HoneyDrunk.Identity.Persistence.Entities;

/// <summary>Durable account lifecycle coordination state.</summary>
public sealed class ErasureMarkerEntity
{
    /// <summary>Gets or sets opaque non-reusable account identifier.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets verified live-erasure instant; expires 35 days later.</summary>
    public DateTimeOffset ErasedAt { get; set; }
}
