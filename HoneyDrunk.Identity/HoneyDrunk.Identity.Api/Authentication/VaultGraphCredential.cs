using Azure.Core;
using Azure.Identity;
using HoneyDrunk.Vault.Abstractions;
using HoneyDrunk.Vault.Models;
using System.Security.Cryptography.X509Certificates;

namespace HoneyDrunk.Identity.Api.Authentication;

/// <summary>Uses the shared Vault store to refresh an external-tenant Graph certificate without persisting its key.</summary>
/// <param name="store">The node's cached, versionless secret store.</param>
/// <param name="secretName">Key Vault certificate secret name.</param>
/// <param name="tenantId">Customer directory tenant ID.</param>
/// <param name="clientId">Graph application client ID in that directory.</param>
public sealed class VaultGraphCredential(ISecretStore store, string secretName, string tenantId, string clientId) : TokenCredential, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private X509Certificate2? certificate;
    private ClientCertificateCredential? credential;
    private string? version;

    /// <inheritdoc />
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var secret = await store.GetSecretAsync(new SecretIdentifier(secretName), cancellationToken);
            if (credential is null || version != secret.Version)
            {
                var replacement = GraphCredentialRegistration.LoadDeployedCertificate(secret.Value);
                certificate?.Dispose();
                certificate = replacement;
                credential = new ClientCertificateCredential(tenantId, clientId, certificate);
                version = secret.Version;
            }

            return await credential.GetTokenAsync(requestContext, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Certificate/parser/provider details must never escape into request logs.
            throw new AuthenticationFailedException("The Graph certificate credential is unavailable.");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        certificate?.Dispose();
        gate.Dispose();
    }
}
