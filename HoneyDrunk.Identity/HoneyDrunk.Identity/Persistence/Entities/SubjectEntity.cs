namespace HoneyDrunk.Identity.Persistence.Entities;

/// <summary>Maps a verified issuer-subject pair to one stable user.</summary>
public sealed class SubjectEntity
{
    /// <summary>Gets or sets the SHA-256 digest of the canonical issuer-subject pair.</summary>
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the verified provider issuer.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Gets or sets the verified opaque subject, retained only until erasure.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Gets or sets the verified provider directory object ID.</summary>
    public string? ObjectId { get; set; }

    /// <summary>Gets or sets the referenced stable user identifier.</summary>
    public string UserId { get; set; } = string.Empty;
}
