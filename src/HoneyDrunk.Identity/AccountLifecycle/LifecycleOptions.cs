namespace HoneyDrunk.Identity.AccountLifecycle;

/// <summary>Environment-owned consumer registry; never accepted from public request bodies.</summary>
public sealed class LifecycleOptions
{
    /// <summary>Gets or sets a value indicating whether private delivery is configured.</summary>
    public bool DeliveryEnabled { get; set; }

    /// <summary>Gets registered consumer node IDs and private queue destinations.</summary>
    public Dictionary<string, string> Consumers { get; init; } = new(StringComparer.Ordinal);
}
