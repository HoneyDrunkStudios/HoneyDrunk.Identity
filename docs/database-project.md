# SQL database project

Open `HoneyDrunk.Identity/HoneyDrunk.Identity.slnx` and expand `HoneyDrunk.Identity.Database`. Its `.sqlproj` builds `bin/Debug/HoneyDrunk.Identity.Database.dacpac` and loads as a traditional SSDT project in Visual Studio 2026. Use the normal SQL Server Data Tools component and .NET Framework 4.8 targeting pack. Framework is only a SQL build-tool dependency; the Identity API remains on .NET 10.

Only command-line `dotnet` builds conditionally import the pinned Microsoft.Build.Sql SDK. Visual Studio uses its installed SSDT targets. Both paths share the explicit SQL file list and DACPAC output, with separate intermediate directories. Add new SQL files through Visual Studio or explicitly include them in the project. No SDK-style Visual Studio extension is required.

`Tables/dbo` contains one SQL file per Identity table with its keys and indexes. `Tables/outbox` and `Schemas` hold the shared outbox objects. `Data/Seed` is for repeatable, explicitly included seed scripts, while `Data/AdHoc` holds manual scripts that never run automatically. `Data/PostDeployment.sql` is the publish entry point; Identity currently requires no seed accounts.

EF remains in `HoneyDrunk.Identity/Persistence`: `Entities` are database records and `Configurations` contain Fluent API mappings. Domain/contracts do not gain the Entity suffix. The SQL project owns DDL; migrations and design-time factories have been removed. Keep mappings and SQL together in each schema change.

## Local deployment

With the `PocketQuests` LocalDB instance running, execute from this repository:

```powershell
./scripts/Deploy-LocalDatabase.ps1                 # Build and generate a SQL plan
./scripts/Deploy-LocalDatabase.ps1 -Action DeployReport
./scripts/Deploy-LocalDatabase.ps1 -Action Publish # Apply to HoneyDrunkIdentity
```

The committed profile targets `(localdb)\PocketQuests`, database `HoneyDrunkIdentity`, using Windows authentication. Pocket Quests has a separate database and SQL project, even when both share this SQL Server instance. No API performs schema deployment on startup.

Publishing blocks possible data loss, preserves objects absent from the project, leaves database options alone, and requests transactional deployment scripts. Back up existing data first. Historical `__EFMigrationsHistory` tables may remain but no longer control deployment. Use an explicitly reviewed deployment profile and protected credentials for a hosted target; never repoint the local profile at production. Review the target-specific script/report before applying it. Database recovery uses a tested backup plan, not an assumed reverse DACPAC deployment.

Integration tests deploy this DACPAC into unique databases before exercising the EF mappings and Identity API. Local Windows tests use SQL Server 2019 LocalDB; Linux tests use the existing SQL Server container fixture. The project targets SQL Server 2019-compatible objects.

See Microsoft's [SQL project deployment-script documentation](https://learn.microsoft.com/en-us/sql/tools/sql-database-projects/concepts/pre-post-deployment-scripts) for adding seed scripts explicitly.
