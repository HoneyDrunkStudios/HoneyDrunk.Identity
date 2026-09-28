using HoneyDrunk.Auth.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HoneyDrunk.Identity.Providers.Entra;

/// <summary>Obtains trusted Entra public signing keys through HTTPS OIDC discovery.</summary>
public sealed class EntraSigningKeys : ISigningKeyProvider
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? discovery;
    private readonly string audience;
    private readonly string issuer;

    /// <summary>Initializes a new instance of the <see cref="EntraSigningKeys"/> class.</summary>
    /// <param name="configuration">Authority and audience supplied by the host.</param>
    public EntraSigningKeys(IConfiguration configuration)
    {
        var authority = configuration["Entra:Authority"]?.TrimEnd('/') ?? string.Empty;
        audience = configuration["Entra:Audience"] ?? string.Empty;
        issuer = configuration["Entra:Issuer"] ?? string.Empty;
        if (authority.Length > 0)
        {
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidOperationException("Entra authority must use HTTPS.");
            discovery = new(authority + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true });
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default) =>
        discovery is null ? [] : (await discovery.GetConfigurationAsync(cancellationToken)).SigningKeys.ToArray();

    /// <inheritdoc />
    public Task<string> GetIssuerAsync(CancellationToken cancellationToken = default) => Task.FromResult(issuer);

    /// <inheritdoc />
    public Task<string> GetAudienceAsync(CancellationToken cancellationToken = default) => Task.FromResult(audience);

    /// <inheritdoc />
    public Task<TimeSpan> GetClockSkewAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeSpan.FromSeconds(30));

    /// <summary>Requests a throttled discovery refresh after token validation fails.</summary>
    public void RequestRefresh() => discovery?.RequestRefresh();
}
