namespace HoneyDrunk.Identity.Api.Configuration;

/// <summary>Configures exact browser origins, with HTTP loopback allowed only for local development.</summary>
public static class CorsRegistration
{
    /// <summary>Registers the configured browser policy and rejects invalid deployed origins.</summary>
    /// <param name="services">Application services.</param>
    /// <param name="configuration">Host configuration.</param>
    /// <param name="environment">The current host environment.</param>
    public static void AddIdentityCors(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.GetLeftPart(UriPartial.Authority) != origin))
            throw new InvalidOperationException("Cors:AllowedOrigins must contain exact HTTPS origins (loopback is allowed only in Development).");
        services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
    }
}
