using HoneyDrunk.Auth.Secrets;
using HoneyDrunk.Identity.AccountLifecycle;
using HoneyDrunk.Identity.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;

namespace HoneyDrunk.Identity.Tests.Fixtures;

internal sealed class IdentityTestHost(string connection, bool configured = false) : WebApplicationFactory<IdentityApiProgram>
{
    private static readonly SymmetricSecurityKey Key = new(RandomNumberGenerator.GetBytes(64));

    public HttpClient Client(string subject = "alice", string audience = "identity", string issuer = "https://identity.test", bool wrongKey = false, bool expired = false, bool includeSubject = true, DateTimeOffset? authenticatedAt = null, string? scope = "access_as_user")
    {
        var signingKey = wrongKey ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)) : Key;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity((includeSubject ? new[] { new Claim("sub", subject) } : []).Concat(scope is null ? [] : new[] { new Claim("scp", scope) }).Concat(authenticatedAt is { } at ? [new Claim("auth_time", at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)] : [])),
            IssuedAt = DateTime.UtcNow.AddMinutes(-20),
            NotBefore = DateTime.UtcNow.AddMinutes(-20),
            Expires = DateTime.UtcNow.AddMinutes(expired ? -10 : 10),
            SigningCredentials = new(signingKey, SecurityAlgorithms.HmacSha256),
        });
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:identity"] = connection,
            ["Entra:Authority"] = configured ? "https://identity.test" : null,
            ["Entra:Issuer"] = configured ? "https://identity.test" : null,
            ["Entra:Audience"] = configured ? "identity" : null,
            ["Entra:MobileClientId"] = configured ? "native-client" : null,
            ["Entra:ApiScope"] = configured ? "api://identity/access_as_user" : null,
        }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ISigningKeyProvider>(new TestKeys());
            services.AddSingleton<HoneyDrunk.Identity.Abstractions.Authentication.IExternalAccounts>(new TestExternalAccounts());
            services.PostConfigure<LifecycleOptions>(options =>
            {
                options.DeliveryEnabled = true;
                options.Consumers["pocketquests"] = "test-only-private-queue";
            });
        });
    }

    private sealed class TestKeys : ISigningKeyProvider
    {
        public Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SecurityKey>>([Key]);

        public Task<string> GetIssuerAsync(CancellationToken cancellationToken = default) => Task.FromResult("https://identity.test");

        public Task<string> GetAudienceAsync(CancellationToken cancellationToken = default) => Task.FromResult("identity");

        public Task<TimeSpan> GetClockSkewAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeSpan.Zero);
    }
}
