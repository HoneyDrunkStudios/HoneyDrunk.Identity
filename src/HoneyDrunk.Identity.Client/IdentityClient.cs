using HoneyDrunk.Identity.Abstractions;
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
}
