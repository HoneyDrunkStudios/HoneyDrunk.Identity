namespace HoneyDrunk.Identity.Abstractions;

/// <summary>An external subject whose signature, issuer, audience and expiry were already validated.</summary>
/// <param name="Issuer">Exact trusted issuer; subjects from different issuers are distinct identities.</param>
/// <param name="Subject">Opaque provider subject; never an email address used for account linking.</param>
public sealed record ExternalSubject(string Issuer, string Subject);
