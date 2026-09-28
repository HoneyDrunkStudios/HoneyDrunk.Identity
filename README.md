# HoneyDrunk.Identity

Shared user identity service for Pocket Quests and future consumers. September 28, 2026: shared service foundation under review in the public [Identity repository](https://github.com/HoneyDrunkStudios/HoneyDrunk.Identity). No package or service has been released, and providers are not enrolled. This is a narrow foundation, not completion of every proposed Identity ADR contract.

## Implemented boundary

`GET /users/me` accepts an access token validated by HoneyDrunk.Auth against the configured issuer, audience, lifetime and Entra OIDC public signing keys. It resolves the verified issuer/subject pair under a SQL transaction lock to a stable `usr_` identifier. Issuers remain isolated; email matching does not link accounts. Inactive directory rows are rejected. The service stores no user passwords, provider client secrets or raw tokens.

`HoneyDrunk.Identity.Abstractions` defines the implemented subject/account/directory seam. `HoneyDrunk.Identity.Client` calls the HTTP boundary and contains no Entra SDK. Both have local candidate version `0.1.0-alpha.2`; run `scripts/Pack-Client.ps1 -OutputDirectory PATH` to produce package files. Nothing is published by this script. A reviewed immutable release to NuGet.org is a prerequisite for ordinary remote consumer restores. Only Client and Abstractions are packable; runtime, provider and API projects remain service implementation.

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

## Consolidated evening handoff

The founder is at work; all human approvals and enrollment remain deferred. Do not interpret this checklist as authorization to submit terms, spend money or create the tenant.

1. **Entra tenant billing decision.** Infrastructure has a validated `Microsoft.AzureActiveDirectory/ciamDirectories` template and a what-if showing one new directory. Confirm the linked Azure subscription, tenant domain/display name and billing before creation. No tenant or Azure provider registration was performed by this implementation.
2. **App registrations and flow.** Create the Identity API audience/scope and public native client, register the actual development/production redirect URIs, use PKCE with no native client secret, and configure the customer sign-in flow. Preserve separate dev/prod audiences and redirects.
3. **Personal Microsoft.** Configure the consumer Microsoft-account federation flow; a workforce organizational login is not a substitute. Verify a real personal Microsoft account end-to-end.
4. **Google.** The founder has no Google Cloud project. Human account access/consent is needed before creating the OAuth project/client and registering the tenant-specific redirects. Store the confidential federation credential only in the appropriate server/provider configuration.
5. **Apple.** The founder has no Apple Developer membership. Enrollment, any fee and legal terms require the founder. Then configure App ID/Services ID, team/key IDs and the Apple federation key/secret. Record the renewal process and test the exact redirect. Do not put the Apple key in this repository, chat or mobile build.
6. **Device validation.** The founder's device is an iPhone 14, user-reported iOS 26.7; Android Studio emulator is the Android target. Register/sign a development build, verify a reachable trusted API/Identity route, then test provider sign-in, expiry, same-account retry, sign-out/account switching and process restart. No physical device or emulator was exercised here.
7. **Hosted operations.** Wire any actual server secret requirements through Vault/managed identity, configure production SQL/network/backup/retention and Pulse exports/alerts, then prove discovery/key rotation, outage behavior and recovery. The current local build is not production deployment approval.
8. **GitHub/Sonar owner access.** Add HoneyDrunk.Identity to the existing SonarQube Cloud GitHub App installation and complete project onboarding. The API returned HTTP 403 for this scope change; the required Sonar check remains enabled. The existing SONAR_TOKEN is already scoped to Identity and was not rotated.

Authoritative setup references: [Entra customer authentication methods](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-authentication-methods-customers), [personal Microsoft federation](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-microsoft-accounts-federation-customers), [Google federation](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-google-federation-customers), [Apple federation](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-apple-federation-customers).

## Release prerequisites

Formally reconcile proposed ADR-0060/ADR-0078 and node/catalog registration; finish supported lifecycle contracts rather than publishing placeholder seams; review/package immutable Client and Abstractions artifacts; consume them in Pocket Quests with locked restore; release the service independently with controlled schema rollout; verify live sign-in and account ownership before calling the user flow ready. Repository creation and the implementation PR are authorized. Merge, release tags, NuGet publication and cloud deployment remain separate actions. See [repository delivery](docs/repository-delivery.md) for CI, protection and release evidence.
