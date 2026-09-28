namespace HoneyDrunk.Identity;

/// <summary>Maps a verified issuer-subject pair to one stable user.</summary>
public sealed class SubjectRow
{
    /// <summary>Gets or sets the SHA-256 digest of the canonical issuer-subject pair.</summary>
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the referenced stable user identifier.</summary>
    public string UserId { get; set; } = string.Empty;
}
