using Azure.Core;
using Azure.Identity;
using HoneyDrunk.Vault.Abstractions;
using System.Security.Cryptography.X509Certificates;

namespace HoneyDrunk.Identity.Api.Authentication;

/// <summary>Selects an explicit server credential for the tenant that owns the customer directory.</summary>
public static class GraphCredentialRegistration
{
    /// <summary>Registers the server credential used for Microsoft Graph account checks.</summary>
    /// <param name="services">Application services.</param>
    /// <param name="configuration">Host configuration, including local user secrets.</param>
    /// <param name="environment">The current host environment.</param>
    public static void AddGraphCredential(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var thumbprint = configuration["Entra:Graph:CertificateThumbprint"];
        var mode = configuration["Entra:Graph:CredentialMode"];
        if (string.Equals(mode, "Certificate", StringComparison.Ordinal))
        {
            var secretName = configuration["Entra:Graph:CertificateSecretName"];
            if (string.IsNullOrWhiteSpace(secretName))
                throw new InvalidOperationException("Configure Entra:Graph:CertificateSecretName with a versionless Key Vault certificate secret name.");
            services.AddSingleton<TokenCredential>(sp => new VaultGraphCredential(
                sp.GetRequiredService<ISecretStore>(),
                secretName,
                RequiredId(configuration, "Entra:Graph:TenantId"),
                RequiredId(configuration, "Entra:Graph:ClientId")));
            return;
        }

        if (!string.IsNullOrWhiteSpace(mode) && mode != "ManagedIdentity")
            throw new InvalidOperationException("Entra:Graph:CredentialMode must be Certificate or ManagedIdentity.");
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && string.IsNullOrWhiteSpace(mode))
            throw new InvalidOperationException("Configure Entra:Graph:CredentialMode explicitly for the directory tenant. Direct managed identity cannot cross tenants.");
        if (mode == "ManagedIdentity" || !environment.IsDevelopment() || string.IsNullOrWhiteSpace(thumbprint))
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

    internal static X509Certificate2 LoadDeployedCertificate(string encoded)
    {
        try
        {
            // Key Vault's certificate secret contains base64 PKCS#12 with an empty password.
            // EphemeralKeySet avoids persisting a private key in the container filesystem.
            var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(encoded), null, X509KeyStorageFlags.EphemeralKeySet);
            ValidateCertificate(certificate);
            return certificate;
        }
        catch (Exception error) when (error is FormatException or System.Security.Cryptography.CryptographicException)
        {
            // Do not retain parser exceptions or credential material in startup diagnostics.
            throw new InvalidOperationException("The Graph certificate secret is not a valid base64 PKCS#12 certificate.");
        }
    }

    private static string RequiredId(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id.ToString("D")
            : throw new InvalidOperationException($"Configure {key} with the directory tenant or app registration ID.");
    }

    private static X509Certificate2 LoadCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        if (matches.Count != 1)
            throw new InvalidOperationException("The configured development certificate was not found in CurrentUser/My.");
        var certificate = matches[0];
        ValidateCertificate(certificate);
        return certificate;
    }

    private static void ValidateCertificate(X509Certificate2 certificate)
    {
        var now = DateTime.UtcNow;
        if (!certificate.HasPrivateKey || now < certificate.NotBefore.ToUniversalTime() || now >= certificate.NotAfter.ToUniversalTime())
        {
            certificate.Dispose();
            throw new InvalidOperationException("The Graph certificate needs a private key and a current validity period.");
        }
    }
}
