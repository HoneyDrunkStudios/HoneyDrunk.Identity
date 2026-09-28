using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace HoneyDrunk.Identity.Tests;

/// <summary>Provides real SQL Server for both local Windows and Linux CI tests.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? container;
    private string connection = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            connection = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true";
            return;
        }

        container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await container.StartAsync();
        connection = container.GetConnectionString();
    }

    /// <summary>Creates a unique database connection, never reusing a development database.</summary>
    /// <returns>The isolated test database connection.</returns>
    public string NewDatabaseConnection() => new SqlConnectionStringBuilder(connection)
    {
        InitialCatalog = $"Identity_Tests_{Guid.NewGuid():N}",
    }.ConnectionString;

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}
