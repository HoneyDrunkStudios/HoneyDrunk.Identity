namespace HoneyDrunk.Identity;

/// <summary>The durable, provider-independent account row.</summary>
public sealed class UserRow
{
    /// <summary>Gets or sets the stable usr-prefixed identifier.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the account lifecycle state.</summary>
    public string State { get; set; } = "Active";

    /// <summary>Gets or sets the first resolution timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
