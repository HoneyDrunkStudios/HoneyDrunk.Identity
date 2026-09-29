namespace HoneyDrunk.Identity.Abstractions.Authentication;

/// <summary>Trusted claims from a validated provider token; token issuance is not reauthentication.</summary>
/// <param name="Subject">Validated issuer, subject and provider object ID.</param>
/// <param name="AuthenticatedAt">Verified auth_time, never substituted with iat.</param>
public sealed record VerifiedLogin(ExternalSubject Subject, DateTimeOffset? AuthenticatedAt);
