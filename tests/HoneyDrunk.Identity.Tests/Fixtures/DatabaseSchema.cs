using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.SqlServer.Dac;

namespace HoneyDrunk.Identity.Tests.Fixtures;

/// <summary>Deploys the production SQL project into isolated integration-test databases.</summary>
internal static class DatabaseSchema
{
    private static readonly SemaphoreSlim DeploymentGate = new(1, 1);

    internal static async Task DeployAsync(DatabaseFacade database)
    {
        // Concurrent DacFx CREATE DATABASE operations compete for SQL's exclusive model lock.
        // Serialize fixture provisioning; application concurrency tests remain unchanged.
        await DeploymentGate.WaitAsync();
        try
        {
            await Task.Run(() => Deploy(database));
        }
        finally
        {
            DeploymentGate.Release();
        }
    }

    private static void Deploy(DatabaseFacade database)
    {
        var connection = new SqlConnectionStringBuilder(database.GetConnectionString());
        if (!connection.InitialCatalog.StartsWith("Identity_Tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("DACPAC test deployment requires an isolated test database.");

        using var package = DacPackage.Load(Path.Join(AppContext.BaseDirectory, "HoneyDrunk.Identity.Database.dacpac"));
        var service = new DacServices(connection.ConnectionString);
        service.Deploy(package, connection.InitialCatalog, upgradeExisting: true, options: new DacDeployOptions
        {
            BlockOnPossibleDataLoss = true,
            DropObjectsNotInSource = false,
            ScriptDatabaseOptions = false,
            IncludeTransactionalScripts = true,
        });

        // A pre-deployment readiness probe can leave a cached login failure for this database.
        SqlConnection.ClearPool((SqlConnection)database.GetDbConnection());
    }
}
