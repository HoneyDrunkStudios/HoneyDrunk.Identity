using Azure.Core;
using Azure.Identity;
using HoneyDrunk.Identity.Api.Authentication;
using HoneyDrunk.Vault.Abstractions;
using HoneyDrunk.Vault.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HoneyDrunk.Identity.Tests.Providers;

/// <summary>Protects the boundary between local certificate credentials and deployed managed identity.</summary>
public sealed class GraphCredentialRegistrationTests
{
    /// <summary>A development certificate setting must never switch production away from managed identity.</summary>
    [Fact]
    public void ProductionIgnoresDevelopmentCertificate()
    {
        using var host = CreateHost(Environments.Production, new() { ["Entra:Graph:CredentialMode"] = "ManagedIdentity", ["Entra:Graph:CertificateThumbprint"] = "not-a-certificate" });
        Assert.IsType<ManagedIdentityCredential>(host.Services.GetRequiredService<TokenCredential>());
    }

    /// <summary>Unconfigured development retains managed identity without accessing the certificate store.</summary>
    [Fact]
    public void NoCertificateRetainsManagedIdentity()
    {
        using var host = CreateHost(Environments.Development, []);
        Assert.IsType<ManagedIdentityCredential>(host.Services.GetRequiredService<TokenCredential>());
    }

    /// <summary>A partially configured local credential must fail rather than silently select another identity.</summary>
    [Fact]
    public void LocalCertificateRequiresExplicitTenantAndClient()
    {
        using var host = CreateHost(Environments.Development, new() { ["Entra:Graph:CertificateThumbprint"] = "not-a-certificate" });
        var error = Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<TokenCredential>());
        Assert.Contains("Entra:Graph:TenantId", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Cloud hosts never silently select an identity in the wrong tenant.</summary>
    /// <param name="mode">Missing or invalid deployment mode.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("Typo")]
    public void DeployedModeMustBeExplicit(string? mode)
    {
        Assert.Throws<InvalidOperationException>(() => CreateHost(Environments.Production, new() { ["Entra:Graph:CredentialMode"] = mode }));
    }

    /// <summary>Certificate parsing failures must not expose secret contents or fall back to another credential.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task DeployedCertificateRejectsMalformedSecret()
    {
        using var host = CreateHost(Environments.Production, new()
        {
            ["Entra:Graph:CredentialMode"] = "Certificate",
            ["Entra:Graph:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["Entra:Graph:ClientId"] = "22222222-2222-2222-2222-222222222222",
            ["Entra:Graph:CertificateSecretName"] = "graph-certificate",
        });
        var credential = host.Services.GetRequiredService<TokenCredential>();
        Assert.IsType<VaultGraphCredential>(credential);
        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(async () =>
            await credential.GetTokenAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), CancellationToken.None));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("sensitive-invalid-certificate", error.Message, StringComparison.Ordinal);
    }

    private static IHost CreateHost(string environment, Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment, DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton<ISecretStore>(new InvalidCertificateStore());
        builder.Services.AddGraphCredential(builder.Configuration, builder.Environment);
        return builder.Build();
    }

    private sealed class InvalidCertificateStore : ISecretStore
    {
        public Task<SecretValue> GetSecretAsync(SecretIdentifier identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecretValue(identifier, "sensitive-invalid-certificate", "test-version"));

        public Task<IReadOnlyList<SecretVersion>> ListSecretVersionsAsync(string secretName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretVersion>>([]);
    }
}
