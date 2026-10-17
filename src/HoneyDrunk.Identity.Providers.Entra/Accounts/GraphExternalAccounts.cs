using Azure.Core;
using HoneyDrunk.Identity.Abstractions.Authentication;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HoneyDrunk.Identity.Providers.Entra.Accounts;

/// <summary>Managed-identity Graph credential lifecycle; never trusts client-supplied object identifiers.</summary>
public sealed class GraphExternalAccounts(HttpClient http, TokenCredential credential, IConfiguration configuration) : IExternalAccounts
{
    private readonly Uri baseUri = ReadBaseUri(configuration);

    /// <inheritdoc />
    public async Task<bool> Exists(ExternalSubject subject, CancellationToken token)
    {
        var id = ObjectId(subject);
        using var response = await Send(HttpMethod.Get, $"users/{id}?$select=id,accountEnabled", token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        var value = await response.Content.ReadFromJsonAsync<JsonElement>(token);
        return value.TryGetProperty("accountEnabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
    }

    /// <inheritdoc />
    public async Task Revoke(ExternalSubject subject, CancellationToken token)
    {
        using var response = await Send(HttpMethod.Post, $"users/{ObjectId(subject)}/revokeSignInSessions", token);
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task Erase(ExternalSubject subject, CancellationToken token)
    {
        var id = ObjectId(subject);
        using var removed = await Send(HttpMethod.Delete, $"users/{id}", token);
        if (removed.StatusCode != HttpStatusCode.NotFound)
            removed.EnsureSuccessStatusCode();
        using var purged = await Send(HttpMethod.Delete, $"directory/deletedItems/{id}", token);
        if (purged.StatusCode != HttpStatusCode.NotFound)
            purged.EnsureSuccessStatusCode();
        using var live = await Send(HttpMethod.Get, $"users/{id}?$select=id", token);
        using var recoverable = await Send(HttpMethod.Get, $"directory/deletedItems/{id}", token);
        if (live.StatusCode != HttpStatusCode.NotFound || recoverable.StatusCode != HttpStatusCode.NotFound)
            throw new HttpRequestException("Provider erasure is not yet verified.");
    }

    private static Uri ReadBaseUri(IConfiguration configuration)
    {
        if (!Uri.TryCreate(configuration["Entra:Graph:BaseUri"], UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
            || uri.AbsolutePath != "/v1.0/"
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Entra:Graph:BaseUri must select the trusted Microsoft Graph HTTPS v1.0 endpoint.");
        return uri;
    }

    private string ObjectId(ExternalSubject subject)
    {
        if (subject.Issuer != configuration["Entra:Issuer"] || !Guid.TryParse(subject.ObjectId, out var id) || id == Guid.Empty)
            throw new UnauthorizedAccessException("A verified provider object ID from the configured issuer is required.");
        return id.ToString("D");
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var access = await credential.GetTokenAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), timeout.Token);
            using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
            return await http.SendAsync(request, timeout.Token);
        }
        catch (Azure.Identity.AuthenticationFailedException)
        {
            throw new HttpRequestException("Provider service credential is unavailable.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new HttpRequestException("Provider service request timed out.");
        }
    }
}
