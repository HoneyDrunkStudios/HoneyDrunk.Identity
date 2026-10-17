using HoneyDrunk.Auth.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace HoneyDrunk.Identity.Providers.Entra.Authentication;

/// <summary>Bridges HoneyDrunk.Auth validation into ASP.NET authentication.</summary>
public sealed class EntraBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IAuthenticationProvider validator, EntraSigningKeys keys) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var credential = AuthCredential.Bearer(header[7..].Trim());
        var result = await validator.AuthenticateAsync(credential, Context.RequestAborted);
        if (!result.IsAuthenticated || result.Identity is null)
        {
            // ConfigurationManager throttles refresh; never trusts claims from an invalid token.
            keys.RequestRefresh();
            return AuthenticateResult.Fail("Invalid or expired identity token.");
        }

        var claims = result.Identity.Claims.SelectMany(c => c.Value.Select(v => new Claim(c.Key, v))).ToArray();
        if (!claims.Any(c => c.Type == "sub") || !claims.Any(c => c.Type == "iss"))
            return AuthenticateResult.Fail("Required identity claims are missing.");
        if (!EntraDelegatedAccess.IsGranted(claims))
            return AuthenticateResult.Fail("Required delegated API scope is missing.");
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }
}
