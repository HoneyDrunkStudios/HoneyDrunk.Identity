using HoneyDrunk.Auth;
using HoneyDrunk.Auth.Abstractions;
using HoneyDrunk.Auth.Authentication;
using HoneyDrunk.Auth.Secrets;
using HoneyDrunk.Identity.Providers.Entra.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace HoneyDrunk.Identity.Providers.Entra.Configuration;

/// <summary>Composes Entra discovery with shared Auth, Kernel and Audit dependencies.</summary>
public static class EntraRegistration
{
    /// <summary>Registers the Entra bearer scheme with scoped authentication dependencies.</summary>
    /// <param name="services">Host services, including durable Audit and Kernel telemetry.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddEntraValidation(this IServiceCollection services)
    {
        // Compose existing Auth's validator explicitly: public OIDC signing keys do
        // not need the default Vault symmetric-key provider. Kernel and durable Audit
        // are required host dependencies; no NullAuditLog fallback is registered.
        services.AddOptions<AuthOptions>();
        services.AddSingleton<EntraSigningKeys>();
        services.AddSingleton<ISigningKeyProvider>(sp => sp.GetRequiredService<EntraSigningKeys>());
        services.AddScoped<IAuthenticationProvider, BearerTokenAuthenticationProvider>();
        services.AddAuthentication("Entra").AddScheme<AuthenticationSchemeOptions, EntraBearerHandler>("Entra", _ => { });
        return services;
    }
}
