using System.Security.Claims;

namespace HoneyDrunk.Identity.Providers.Entra.Authentication;

/// <summary>Requires the delegated permission exposed by the Identity API.</summary>
public static class EntraDelegatedAccess
{
    /// <summary>Checks validated claims for the exact delegated API scope.</summary>
    /// <param name="claims">Claims from a cryptographically validated token.</param>
    /// <returns>Whether the token grants delegated API access.</returns>
    public static bool IsGranted(IEnumerable<Claim> claims) => claims
        .Where(claim => claim.Type == "scp")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Contains("access_as_user", StringComparer.Ordinal);
}
