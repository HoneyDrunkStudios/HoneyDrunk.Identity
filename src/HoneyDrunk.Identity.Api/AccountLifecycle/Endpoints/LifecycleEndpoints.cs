using HoneyDrunk.Auth.Abstractions;
using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Identity.Providers.Entra.Authentication;
using System.Security.Claims;

namespace HoneyDrunk.Identity.Api.AccountLifecycle.Endpoints;

/// <summary>Owner-only lifecycle routes; worker erasure and acknowledgments have no public HTTP route.</summary>
public static class LifecycleEndpoints
{
    /// <summary>Registers authenticated owner operations.</summary>
    /// <param name="app">Identity host.</param>
    public static void MapLifecycle(this WebApplication app)
    {
        var routes = app.MapGroup("/users/me").RequireAuthorization();
        routes.MapGet("/status", (ClaimsPrincipal principal, SqlAccountLifecycle lifecycle, CancellationToken token) =>
            Safe(async () => await lifecycle.Status(Login(principal).Subject, token)));
        routes.MapPost("/deletion", (Confirmation request, ClaimsPrincipal principal, SqlAccountLifecycle lifecycle, CancellationToken token) =>
            Safe(async () => await lifecycle.Request(Login(principal), request.Confirmed, token)));
        routes.MapPost("/recovery", (Confirmation request, ClaimsPrincipal principal, SqlAccountLifecycle lifecycle, CancellationToken token) =>
            Safe(async () => await lifecycle.Cancel(Login(principal), request.Confirmed, token)));
        routes.MapPost("/link", (LinkProof request, ClaimsPrincipal principal, SqlAccountLifecycle lifecycle, IAuthenticationProvider validator, CancellationToken token) =>
            Safe(async () => await lifecycle.Link(Login(principal), await Additional(request.AccessToken, validator, token), token)));
        routes.MapPost("/unlink", (LinkProof request, ClaimsPrincipal principal, SqlAccountLifecycle lifecycle, IAuthenticationProvider validator, CancellationToken token) =>
            Safe(async () =>
            {
                await lifecycle.Unlink(Login(principal), await Additional(request.AccessToken, validator, token), token);
                return await lifecycle.Status(Login(principal).Subject, token);
            }));
    }

    private static VerifiedLogin Login(ClaimsPrincipal principal)
    {
        var issued = long.TryParse(principal.FindFirstValue("auth_time"), out var seconds) && seconds >= 0 && seconds <= 253402300799
            ? DateTimeOffset.FromUnixTimeSeconds(seconds) : (DateTimeOffset?)null;
        return new(new(principal.FindFirstValue("iss") ?? throw new UnauthorizedAccessException(), principal.FindFirstValue("sub") ?? throw new UnauthorizedAccessException(), principal.FindFirstValue("oid")), issued);
    }

    private static async Task<VerifiedLogin> Additional(string accessToken, IAuthenticationProvider validator, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 32768)
            throw new UnauthorizedAccessException("Additional sign-in proof is required.");
        var result = await validator.AuthenticateAsync(AuthCredential.Bearer(accessToken), token);
        if (!result.IsAuthenticated || result.Identity is null)
            throw new UnauthorizedAccessException("Additional sign-in proof is invalid.");
        var claims = result.Identity.Claims.SelectMany(c => c.Value.Select(v => new Claim(c.Key, v))).ToArray();
        if (!EntraDelegatedAccess.IsGranted(claims))
            throw new UnauthorizedAccessException("Additional sign-in proof requires delegated API access.");
        return Login(new ClaimsPrincipal(new ClaimsIdentity(claims)));
    }

    private static async Task<IResult> Safe(Func<Task<AccountStatus>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (ArgumentException error)
        {
            return Results.Problem(error.Message, statusCode: 400);
        }
        catch (InvalidOperationException error)
        {
            return Results.Problem(error.Message, statusCode: 409);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("Provider verification is temporarily unavailable. Retry later.", statusCode: 503);
        }
    }

    /// <summary>An explicit owner confirmation.</summary>
    /// <param name="Confirmed">Whether the owner confirmed the displayed consequence.</param>
    public sealed record Confirmation(bool Confirmed);

    /// <summary>Additional provider proof; never recorded in logs or an audit payload.</summary>
    /// <param name="AccessToken">Fresh independently verified bearer proof.</param>
    public sealed record LinkProof(string AccessToken);
}
