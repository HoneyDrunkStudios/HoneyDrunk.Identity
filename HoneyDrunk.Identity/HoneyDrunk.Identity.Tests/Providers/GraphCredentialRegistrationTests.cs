using Azure.Core;
using Azure.Identity;
using HoneyDrunk.Identity.Api.Authentication;
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
        using var host = CreateHost(Environments.Production, new() { ["Entra:Graph:CertificateThumbprint"] = "not-a-certificate" });
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

    private static IHost CreateHost(string environment, Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment, DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddGraphCredential(builder.Configuration, builder.Environment);
        return builder.Build();
    }
}
