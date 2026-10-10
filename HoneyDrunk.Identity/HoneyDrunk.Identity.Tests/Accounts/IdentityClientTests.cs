using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Client;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HoneyDrunk.Identity.Tests.Accounts;

/// <summary>Verifies the consumer HTTP contract, owner proof, confirmation and failure handling.</summary>
public sealed class IdentityClientTests
{
    /// <summary>Every lifecycle operation preserves the owner header and its distinct body contract.</summary>
    /// <param name="operation">The consumer operation.</param>
    /// <param name="route">Its public HTTP route.</param>
    /// <param name="expectedBody">Its expected JSON body, or null for a status read.</param>
    /// <returns>The completed contract check.</returns>
    [Theory]
    [InlineData("status", "users/me/status", null)]
    [InlineData("delete", "users/me/deletion", "{\"confirmed\":true}")]
    [InlineData("decline-delete", "users/me/deletion", "{\"confirmed\":false}")]
    [InlineData("recover", "users/me/recovery", "{\"confirmed\":true}")]
    [InlineData("decline-recover", "users/me/recovery", "{\"confirmed\":false}")]
    [InlineData("link", "users/me/link", "{\"accessToken\":\"additional-proof\"}")]
    [InlineData("unlink", "users/me/unlink", "{\"accessToken\":\"additional-proof\"}")]
    public async Task LifecyclePreservesRequestContract(string operation, string route, string? expectedBody)
    {
        var expected = new AccountStatus("account-1", "Inactive", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(30), 7);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) };
        using var handler = new ContractHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://identity.example.test/") };
        var client = new IdentityClient(http);

        var actual = await InvokeLifecycle(client, operation);

        Assert.Equal(expected, actual);
        Assert.Equal(route, handler.Uri!.AbsolutePath.TrimStart('/'));
        Assert.Equal(string.Empty, handler.Uri.Query);
        Assert.Equal("Bearer owner-proof", handler.Authorization);
        Assert.Equal(expectedBody is null ? HttpMethod.Get : HttpMethod.Post, handler.Method);
        if (expectedBody is null)
            Assert.Null(handler.Body);
        else
            Assert.True(JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(expectedBody), JsonSerializer.Deserialize<JsonElement>(handler.Body!)));
    }

    /// <summary>Lifecycle authentication and server failures never become a fabricated account status.</summary>
    /// <param name="status">An unsuccessful response.</param>
    /// <returns>The completed failure check.</returns>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task LifecycleFailureRemainsAnError(HttpStatusCode status)
    {
        using var response = new HttpResponseMessage(status);
        using var handler = new ContractHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://identity.example.test/") };
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new IdentityClient(http).Status("owner-proof"));
        Assert.Equal(status, error.StatusCode);
    }

    /// <summary>A successful HTTP response with a missing status is still a protocol failure.</summary>
    /// <returns>The completed payload check.</returns>
    [Fact]
    public async Task MissingStatusFailsClosed()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create<AccountStatus?>(null) };
        using var handler = new ContractHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://identity.example.test/") };
        await Assert.ThrowsAsync<HttpRequestException>(() => new IdentityClient(http).Status("owner-proof"));
    }

    /// <summary>Resolution distinguishes rejected identity from temporary server failures.</summary>
    /// <param name="status">The rejected or failed response.</param>
    /// <returns>The completed resolution check.</returns>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ResolveDistinguishesAuthenticationFromAvailability(HttpStatusCode status)
    {
        using var response = new HttpResponseMessage(status);
        using var handler = new ContractHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://identity.example.test/") };
        var client = new IdentityClient(http);
        if (status == HttpStatusCode.ServiceUnavailable)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.Resolve("owner-proof"));
            Assert.Equal(status, error.StatusCode);
        }
        else
        {
            Assert.Null(await client.Resolve("owner-proof"));
        }
    }

    private static Task<AccountStatus> InvokeLifecycle(IdentityClient client, string operation) => operation switch
    {
        "status" => client.Status("owner-proof"),
        "delete" => client.RequestDeletion("owner-proof", true),
        "decline-delete" => client.RequestDeletion("owner-proof", false),
        "recover" => client.Recover("owner-proof", true),
        "decline-recover" => client.Recover("owner-proof", false),
        "link" => client.Link("owner-proof", "additional-proof"),
        "unlink" => client.Unlink("owner-proof", "additional-proof"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private sealed class ContractHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? Authorization { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
