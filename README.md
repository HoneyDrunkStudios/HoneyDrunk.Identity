# HoneyDrunk.Identity

Shared user identity service for Pocket Quests and future consumers. September 28, 2026: shared service foundation under review in the public [Identity repository](https://github.com/HoneyDrunkStudios/HoneyDrunk.Identity). No package or service has been released, and providers are not enrolled. This is a narrow foundation, not completion of every proposed Identity ADR contract.

## Implemented boundary

`GET /users/me` accepts an access token validated by HoneyDrunk.Auth against the configured issuer, audience, lifetime and Entra OIDC public signing keys. It resolves the verified issuer/subject pair under a SQL transaction lock to a stable `usr_` identifier. Issuers remain isolated; email matching does not link accounts. Inactive directory rows are rejected. The service stores no user passwords, provider client secrets or raw tokens.

`HoneyDrunk.Identity.Abstractions` defines the implemented subject/account/directory seam. `HoneyDrunk.Identity.Client` calls the HTTP boundary and contains no Entra SDK. Both have local candidate version `0.1.0-alpha.3`; run `scripts/Pack-Client.ps1 -OutputDirectory PATH` to produce package files. Nothing is published by this script. A reviewed immutable release to NuGet.org is a prerequisite for ordinary remote consumer restores. Only Client and Abstractions are packable; runtime, provider and API projects remain service implementation.

The runtime uses HoneyDrunk.Data.EntityFramework and SQL Server. Kernel provides request/operation context and scoped telemetry. Auth is scoped to match its dependencies. Audit.Data preserves the full canonical envelope through the Identity audit unit of work. User creation appends canonical audit inside its directory transaction. Legacy prototype audit rows are preserved, and append does not purge records. Pulse exports to Aspire's local OTLP sink. Standards analyzers run with warnings as errors.

No public audit query is exposed. Audit retention and operational-log retention are separate decisions; no production retention enforcement or tamper-evidence guarantee is claimed. Shared Auth logs an audit-write failure without changing its authentication decision; that shared failure policy needs operational monitoring before production.

## Local use

Requires .NET SDK 10.0.401 and SQL Server LocalDB for the Windows development host. Tests automatically use MSSQLLocalDB on Windows and an isolated SQL Server container on Linux (Docker required). They never skip when SQL is unavailable. Create/start the dedicated `PocketQuests` LocalDB instance once, then from this checkout:

```powershell
dotnet tool restore
dotnet restore --locked-mode
dotnet ef database update --project src/HoneyDrunk.Identity
dotnet test
dotnet run --project src/HoneyDrunk.Identity.Api
```

The development database is `HoneyDrunkIdentity` on `(localdb)\PocketQuests`. Port 5218 serves the API. `/health` checks SQL connectivity; it does not assert completed provider enrollment. `/client-configuration` returns 503 until public Entra configuration is supplied. Unconfigured token validation fails closed. Development signing keys exist only in test hosts, never in the runnable API.

Pocket Quests' Aspire AppHost launches this service from an explicit configured source path, or connects to a separately deployed HTTPS service. The product API consumes the versioned Client package by default. Source checkouts can live in unrelated directories.

## Public Entra configuration

The host needs `Entra:Authority` (the exact tenant-specific HTTPS OIDC authority), `Entra:Issuer` (the exact issuer from trusted tenant discovery), `Entra:Audience` (Identity API audience), `Entra:MobileClientId` (public native client) and `Entra:ApiScope` (delegated access scope). Verify the configured issuer against the actual tenant's discovery document and minted token before enabling users. Discovery refresh is throttled; key-rotation recovery still needs a real-provider test.

Native login uses browser-delegated authorization code + PKCE. The mobile app gets public settings from Identity and calls standard OIDC endpoints; it does not hold a client secret. The current foundation is not yet a complete Identity-owned session broker, refresh-token lifecycle, recovery/linking system, profile API or erasure fan-out.

## Human setup tracking

Pending and completed human dependencies are tracked only in Architecture's canonical `initiatives/projects/manual-actions.md` ledger. The Pocket Quests handoff links to the current working-copy file. The ledger is not yet published in Architecture main; once published, its permanent location is [cross-project manual actions](https://github.com/HoneyDrunkStudios/HoneyDrunk.Architecture/blob/main/initiatives/projects/manual-actions.md).

There is no assumed evening deadline and no reminder schedule. Agents continue independent implementation and configuration; the ledger distinguishes personal account access, billing/legal consent and physical feedback from agent-owned work. It records completed actions to prevent repeated requests. No tenant or provider enrollment has occurred.

Authoritative setup references: [Entra customer authentication methods](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-authentication-methods-customers), [personal Microsoft federation](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-microsoft-accounts-federation-customers), [Google federation](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-google-federation-customers), [Apple federation](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-apple-federation-customers).

## Release prerequisites

Formally reconcile proposed ADR-0060/ADR-0078 and node/catalog registration; finish supported lifecycle contracts rather than publishing placeholder seams; review/package immutable Client and Abstractions artifacts; consume them in Pocket Quests with locked restore; release the service independently with controlled schema rollout; verify live sign-in and account ownership before calling the user flow ready. Repository creation and the implementation PR are authorized. Merge, release tags, NuGet publication and cloud deployment remain separate actions. See [repository delivery](docs/repository-delivery.md) for CI, protection and release evidence.
