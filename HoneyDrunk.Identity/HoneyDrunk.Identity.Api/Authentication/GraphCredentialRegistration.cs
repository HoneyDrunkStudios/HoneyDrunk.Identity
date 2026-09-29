using Azure.Core;
using Azure.Identity;
using System.Security.Cryptography.X509Certificates;

namespace HoneyDrunk.Identity.Api.Authentication;

/// <summary>Uses a local Windows certificate in development and managed identity in deployed environments.</summary>
public static class GraphCredentialRegistration
{
    /// <summary>Registers the server credential used for Microsoft Graph account checks.</summary>
    /// <param name="services">Application services.</param>
    /// <param name="configuration">Host configuration, including local user secrets.</param>
    /// <param name="environment">The current host environment.</param>
    public static void AddGraphCredential(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var thumbprint = configuration["Entra:Graph:CertificateThumbprint"];
        if (!environment.IsDevelopment() || string.IsNullOrWhiteSpace(thumbprint))
        {
            services.AddSingleton<TokenCredential>(new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
            return;
        }

        services.AddSingleton(_ => LoadCertificate(thumbprint));
        services.AddSingleton<TokenCredential>(sp => new ClientCertificateCredential(
            RequiredId(configuration, "Entra:Graph:TenantId"),
            RequiredId(configuration, "Entra:Graph:ClientId"),
            sp.GetRequiredService<X509Certificate2>()));
    }

    private static string RequiredId(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id.ToString("D")
            : throw new InvalidOperationException($"Configure {key} with the development app registration ID.");
    }

    private static X509Certificate2 LoadCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        if (matches.Count != 1)
            throw new InvalidOperationException("The configured development certificate was not found in CurrentUser/My.");
        var certificate = matches[0];
        var now = DateTime.UtcNow;
        if (!certificate.HasPrivateKey || now < certificate.NotBefore.ToUniversalTime() || now >= certificate.NotAfter.ToUniversalTime())
        {
            certificate.Dispose();
            throw new InvalidOperationException("The development certificate needs a private key and a current validity period.");
        }

        return certificate;
    }
}
