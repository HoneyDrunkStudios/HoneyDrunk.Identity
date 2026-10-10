using HoneyDrunk.Identity.Abstractions.Accounts;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace HoneyDrunk.Identity.Client;

/// <summary>Resolves the current user through the shared Identity HTTP boundary.</summary>
public sealed class IdentityClient(HttpClient http)
{
    /// <summary>Validates a bearer token with Identity and returns its active account.</summary>
    /// <param name="accessToken">Provider access token; sent only in the authorization header.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The account, or null when Identity rejects authentication.</returns>
    public async Task<UserRecord?> Resolve(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserRecord>(cancellationToken);
    }

    /// <summary>Reads an existing owner account, including inactive recovery status.</summary>
    /// <param name="accessToken">The owner bearer proof.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The current account status.</returns>
    public Task<AccountStatus> Status(string accessToken, CancellationToken cancellationToken = default) => Lifecycle("status", accessToken, null, cancellationToken);

    /// <summary>Requests account inactivation and the original 30-day recovery window.</summary>
    /// <param name="accessToken">The owner bearer proof.</param>
    /// <param name="confirmed">Explicit owner confirmation.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The current account status.</returns>
    public Task<AccountStatus> RequestDeletion(string accessToken, bool confirmed, CancellationToken cancellationToken = default) => Lifecycle("deletion", accessToken, new { confirmed }, cancellationToken);

    /// <summary>Explicitly recovers before the deadline using recent provider authentication.</summary>
    /// <param name="accessToken">The owner bearer proof.</param>
    /// <param name="confirmed">Explicit owner confirmation.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The current account status.</returns>
    public Task<AccountStatus> Recover(string accessToken, bool confirmed, CancellationToken cancellationToken = default) => Lifecycle("recovery", accessToken, new { confirmed }, cancellationToken);

    /// <summary>Links two independently verified recent provider sessions without merging accounts.</summary>
    /// <param name="accessToken">The owner bearer proof.</param>
    /// <param name="additionalAccessToken">Fresh proof for the additional method.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The current account status.</returns>
    public Task<AccountStatus> Link(string accessToken, string additionalAccessToken, CancellationToken cancellationToken = default) => Lifecycle("link", accessToken, new { accessToken = additionalAccessToken }, cancellationToken);

    /// <summary>Removes a verified sign-in method while preserving a different current method.</summary>
    /// <param name="accessToken">The owner bearer proof.</param>
    /// <param name="additionalAccessToken">Fresh proof for the additional method.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The current account status.</returns>
    public Task<AccountStatus> Unlink(string accessToken, string additionalAccessToken, CancellationToken cancellationToken = default) => Lifecycle("unlink", accessToken, new { accessToken = additionalAccessToken }, cancellationToken);

    private async Task<AccountStatus> Lifecycle(string route, string accessToken, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, "users/me/" + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountStatus>(token) ?? throw new HttpRequestException("Identity status is missing.");
    }
}
