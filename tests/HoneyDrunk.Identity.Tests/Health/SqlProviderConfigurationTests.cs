using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HoneyDrunk.Identity.Tests.Health;

/// <summary>Verifies SQL provider compatibility without connecting to a database or identity service.</summary>
public sealed class SqlProviderConfigurationTests
{
    /// <summary>The actual API factory preserves the SQL 150 DACPAC's query capabilities.</summary>
    [Fact]
    public void ApiSqlProviderPreservesSqlServer2019Compatibility()
    {
        using var host = new IdentityTestHost("Server=unused.invalid;Database=identity;Integrated Security=true");
        using var context = host.Services.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContext();
        var sql = context.Users.Select(user => Math.Min(user.UserId.Length, 10)).ToQueryString();

        Assert.Contains("LEN(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LEAST(", sql, StringComparison.Ordinal);
    }

    /// <summary>Managed identity authentication remains available without requesting a token.</summary>
    [Fact]
    public void SqlProviderIncludesManagedIdentityAuthentication()
    {
        var provider = SqlAuthenticationProvider.GetProvider(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity);
        Assert.NotNull(provider);
        Assert.True(provider.IsSupported(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity));
    }
}
