using HoneyDrunk.Identity.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace HoneyDrunk.Identity.Tests.Health;

/// <summary>Verifies deployed browser boundaries and database-independent liveness.</summary>
public sealed class DeploymentConfigurationTests
{
    /// <summary>Only a configured exact HTTPS origin receives a CORS grant.</summary>
    /// <param name="origin">The requesting browser origin.</param>
    /// <param name="allowed">Whether the configured policy should allow it.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData("https://app.example.test", true)]
    [InlineData("https://other.example.test", false)]
    [InlineData("http://localhost:8081", false)]
    public async Task BrowserOriginsAreExplicit(string origin, bool allowed)
    {
        await using var factory = new ConfigurationHost("https://app.example.test");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    /// <summary>Insecure browser origins fail before a deployed host can serve requests.</summary>
    [Fact]
    public void InsecureDeployedOriginIsRejected()
    {
        using var factory = new ConfigurationHost("http://app.example.test");
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private sealed class ConfigurationHost(string origin) : WebApplicationFactory<IdentityApiProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:identity", "Server=unavailable.invalid;Database=identity;Integrated Security=true;Connect Timeout=1");
            builder.UseSetting("Cors:AllowedOrigins:0", origin);
        }
    }
}
