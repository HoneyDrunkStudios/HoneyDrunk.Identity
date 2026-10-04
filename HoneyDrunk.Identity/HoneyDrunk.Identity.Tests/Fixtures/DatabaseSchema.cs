using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.SqlServer.Dac;

namespace HoneyDrunk.Identity.Tests.Fixtures;

/// <summary>Deploys the production SQL project into isolated integration-test databases.</summary>
internal static class DatabaseSchema
{
    internal static Task DeployAsync(DatabaseFacade database) => Task.Run(() =>
    {
        var connection = new SqlConnectionStringBuilder(database.GetConnectionString());
        if (!connection.InitialCatalog.StartsWith("Identity_Tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("DACPAC test deployment requires an isolated test database.");

        using var package = DacPackage.Load(Path.Combine(AppContext.BaseDirectory, "HoneyDrunk.Identity.Database.dacpac"));
        var service = new DacServices(connection.ConnectionString);
        service.Deploy(package, connection.InitialCatalog, upgradeExisting: true, options: new DacDeployOptions
        {
            BlockOnPossibleDataLoss = true,
            DropObjectsNotInSource = false,
            ScriptDatabaseOptions = false,
            IncludeTransactionalScripts = true,
        });
    });
}
