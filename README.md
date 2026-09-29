# HoneyDrunk.Identity

Shared account API for Pocket Quests and future HoneyDrunk applications. The service owns its SQL database; multiple application databases can share one SQL Server instance. This implementation is under review, not a production release.

## Solution and responsibilities

Open `HoneyDrunk.Identity/HoneyDrunk.Identity.slnx` in Visual Studio. Projects sit beside the solution and use responsibility folders internally. Persistence types have an `Entity` suffix and separate Fluent API configurations; public account/domain contracts do not. See [code organization](docs/code-organization.md).

- API and Entra provider: validate issuer, audience, signature, lifetime and the exact delegated `access_as_user` scope before resolving a stable account. Link/unlink proofs use the same scope requirement. User identity is based on issuer/subject, never an email match.
- Runtime: directory resolution, account linking, recent-proof deletion/recovery, durable lifecycle intent delivery and consumer acknowledgments. Deletion fails closed without configured private transport and consumers. Provider and consumer failures retain account access restrictions.
- SQL Server database project: table definitions, data scripts and DACPAC publish profiles. EF Core handles queries and saves, with no EF migration history required for schema deployment.
- Client and Abstractions: independent NuGet candidates, version **0.1.0-alpha.4**. These are not published packages. Runtime/API/provider projects are not packable.

Kernel supplies trusted request context; Auth validates credentials; Data and Audit.Data persist account changes and canonical audit records transactionally; Pulse exports telemetry. Public headers cannot assign internal ownership. Erasure has a scoped audit-data exception and recovery markers; production backup/retention enforcement still requires operational validation.

## Local development

Requires .NET SDK 10.0.401 and SQL Server LocalDB. Visual Studio's SQL project uses SSDT and its .NET Framework 4.8 build tooling; application projects target .NET 10. CLI/CI builds use Microsoft.Build.Sql. See [database deployment](docs/database-project.md).

From the repository root:

```powershell
dotnet tool restore
dotnet restore HoneyDrunk.Identity/HoneyDrunk.Identity.slnx --locked-mode
# Create the dedicated development instance once, then start it.
sqllocaldb create PocketQuests
sqllocaldb start PocketQuests
./scripts/Deploy-LocalDatabase.ps1
# Review the generated deployment SQL first.
./scripts/Deploy-LocalDatabase.ps1 -Action Publish
dotnet test HoneyDrunk.Identity/HoneyDrunk.Identity.Tests --configuration Release
dotnet run --project HoneyDrunk.Identity/HoneyDrunk.Identity.Api
```

The development database is `HoneyDrunkIdentity` on `(localdb)\PocketQuests`. Tests use uniquely named databases on MSSQLLocalDB on Windows or an isolated SQL Server container on Linux. Missing SQL fails the tests instead of skipping them. Pocket Quests' AppHost can start this service from an explicitly configured checkout.

## Sign-in setup

Configure `Entra:Authority`, `Entra:Issuer`, `Entra:Audience`, `Entra:MobileClientId` and `Entra:ApiScope` in local configuration or user secrets. The scope ends in `/access_as_user`. Verify issuer/audience against trusted tenant discovery and app registration. Graph uses a local certificate in Development or managed identity in deployment; no private key or client secret belongs in source or the Expo app. Follow [local Entra sign-in](docs/local-entra-sign-in.md).

An external customer tenant and local registration were configured during development. Those settings and credentials are not bundled in this repository. The hosted user flow determines available sign-in methods; the API does not advertise unconfigured social providers. `/client-configuration` returns 503 if public configuration is missing. `/health` checks SQL connectivity only.

## Review and release

See [repository delivery](docs/repository-delivery.md) for review evidence and remaining gates. Native-device authentication, provider key rotation, production transport/erasure and backup recovery still need environment-specific verification. Local account creation was exercised, but the full authenticated Pocket Quests flow after the schema upgrade is not yet confirmed.

Run `scripts/Pack-Client.ps1 -OutputDirectory PATH` and `scripts/Test-Packages.ps1 -PackageDirectory PATH` to validate isolated package consumption. No script publishes packages. PR publication does not authorize merge, NuGet release or cloud deployment.
