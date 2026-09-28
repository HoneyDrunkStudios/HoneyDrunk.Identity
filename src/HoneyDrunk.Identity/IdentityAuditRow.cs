namespace HoneyDrunk.Identity;

/// <summary>Preserves the legacy prototype audit schema; new events use AuditRecord.</summary>
public sealed class IdentityAuditRow
{
    /// <summary>Gets or sets the original database identifier.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the original event time.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Gets or sets the original event name.</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>Gets or sets the original outcome.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Gets or sets the original pseudonymous actor digest.</summary>
    public string ActorHash { get; set; } = string.Empty;
}
