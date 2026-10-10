using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.Providers.Entra.Accounts;
using HoneyDrunk.Identity.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace HoneyDrunk.Identity.Tests.Providers;

/// <summary>Verifies provider erasure against isolated HTTP doubles, without tenant mutation.</summary>
public sealed class GraphExternalAccountsTests
{
    private const string Issuer = "https://issuer.example.test";
    private static readonly ExternalSubject Subject = new(Issuer, "subject", "c8a731b6-623f-4854-9f1a-7353e1165f64");

    /// <summary>Hard deletion is verified in both live and recoverable directories.</summary>
    /// <returns>The provider boundary test.</returns>
    [Fact]
    public async Task ErasureRequiresBothDirectoriesToBeAbsent()
    {
        using var handler = new GraphTestHandler([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.OK]);
        using var http = new HttpClient(handler);
        var provider = Provider(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.Erase(Subject, default));
        Assert.Equal(new[] { "DELETE /v1.0/users/" + Subject.ObjectId, "DELETE /v1.0/directory/deletedItems/" + Subject.ObjectId, "GET /v1.0/users/" + Subject.ObjectId + "?$select=id", "GET /v1.0/directory/deletedItems/" + Subject.ObjectId }, handler.Requests);
        Assert.All(handler.Hosts, host => Assert.Equal("graph.microsoft.com", host));
        await AssertResponsesDisposed(handler);
    }

    /// <summary>Retries tolerate an already deleted provider object.</summary>
    /// <returns>The idempotency test.</returns>
    [Fact]
    public async Task AlreadyErasedProviderIsIdempotent()
    {
        using var handler = new GraphTestHandler(Enumerable.Repeat(HttpStatusCode.NotFound, 5));
        using var http = new HttpClient(handler);
        await Provider(http).Revoke(Subject, default);
        await Provider(http).Erase(Subject, default);
        Assert.Equal(5, handler.Requests.Count);
        await AssertResponsesDisposed(handler);
    }

    /// <summary>Provider account state must affirmatively permit a new mapping.</summary>
    /// <param name="status">HTTP response status.</param>
    /// <param name="body">Provider response.</param>
    /// <param name="expected">Expected mapping eligibility.</param>
    /// <returns>The existence check.</returns>
    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"accountEnabled\":true}", true)]
    [InlineData(HttpStatusCode.OK, "{\"accountEnabled\":false}", false)]
    [InlineData(HttpStatusCode.NotFound, "{}", false)]
    public async Task MappingRequiresEnabledAccount(HttpStatusCode status, string body, bool expected)
    {
        using var handler = new GraphTestHandler([status], body);
        using var http = new HttpClient(handler);
        Assert.Equal(expected, await Provider(http).Exists(Subject, default));
        await AssertResponsesDisposed(handler);
    }

    /// <summary>Client-selected issuer or invalid object IDs never reach Graph.</summary>
    /// <returns>The ownership validation test.</returns>
    [Fact]
    public async Task UntrustedProviderIdentityIsRejectedBeforeHttp()
    {
        using var handler = new GraphTestHandler([]);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Provider(http).Erase(Subject with { Issuer = "https://attacker.example.test" }, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Provider(http).Erase(Subject with { ObjectId = "../other" }, default));
        Assert.Empty(handler.Requests);
    }

    /// <summary>Transport timeout is retryable but caller cancellation remains cancellation.</summary>
    /// <returns>The cancellation boundary test.</returns>
    [Fact]
    public async Task TimeoutAndCallerCancellationRemainDistinct()
    {
        using var handler = new GraphTestHandler([]) { Cancel = true };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).Exists(Subject, default));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(http).Exists(Subject, canceled.Token));
    }

    private static async Task AssertResponsesDisposed(GraphTestHandler handler)
    {
        Assert.NotEmpty(handler.Responses);
        foreach (var response in handler.Responses)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => response.Content.ReadAsStringAsync());
    }

    private static GraphExternalAccounts Provider(HttpClient http) => new(http, new GraphTestCredential(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Entra:Issuer"] = Issuer }).Build());
}
