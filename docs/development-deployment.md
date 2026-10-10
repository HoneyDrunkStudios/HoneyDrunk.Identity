# Development deployment review

This is a source implementation and an approval plan, not a deployed environment. Resource creation, spending, credentials, consent, permissions, environment protection changes, merge and deployment require separate approval. The first target is basic customer sign-in. Deletion, consumer erasure and backup recovery remain disabled/unaccepted.

## Verified inventory, 2026-10-09; SQL/network recheck 2026-10-10

Read-only Azure and GitHub queries found:

| Item | Observed state |
|---|---|
| Subscription | `honeydrunk-dev`, `82073da5-bd6d-4874-947e-73b791054cbc` |
| Azure workforce tenant | `f5654adb-2a4c-4317-9217-09ef32ccdd3a` |
| Customer tenant | `honeydrunkcustomers.onmicrosoft.com`, `28ad6dfb-254d-41c3-a840-9b2135ed11a6`; succeeded CIAM resource in `rg-hd-identity-dev` |
| Shared ACA environment | `cae-hd-dev`, eastus2, succeeded; Consumption, no VNet configuration |
| Shared registry | `acrhdshareddev.azurecr.io`, Basic, admin account disabled |
| Shared broker | `sb-hd-shared-dev`, Standard, zero queues/topics; public endpoint, local authentication enabled |
| Identity resources | No Identity Web App, SQL server/database or Key Vault found |
| Pulse | `ca-hd-pulse-dev` running, explicit revision `ca-hd-pulse-dev--ca-37998537759-1` at 100%; healthy [release](https://github.com/HoneyDrunkStudios/HoneyDrunk.Pulse/actions/runs/37998537759). Untouched |
| Infrastructure deploy principal | App `0fce147d-3c2a-408d-b122-34af727dddba`; Contributor and User Access Administrator on platform/Pulse resource groups, no Identity resource-group access |
| Identity GitHub configuration | No environments or repository variables returned by the read APIs |

Customer-tenant Graph inventory failed with `AADSTS50076` (MFA required). No interactive login, private-certificate read, app creation or consent was attempted. Existing customer app IDs, certificate expiry/exportability, permissions, user-flow association and redirect URIs are **not verified**. Obtain approved read access before selecting real values. Pulse's join-only role and its app identity grant no Identity access.

## Tenant and credential decision

Direct system-assigned managed identity authenticates in the workforce tenant; it cannot authenticate as the existing customer-tenant Graph app. Microsoft requires a federated managed identity to be **user-assigned and in the same tenant as its trusting app registration**. The documented cross-tenant extension uses a multitenant workforce app and a service principal in the resource tenant. External customer tenants support single-tenant application registrations; suitability of that bridge for this customer tenant has not been established. Do not provision a UAMI, federated trust or multitenant app on this evidence.

The implemented option extends the existing customer-app certificate approach: the API's workforce system identity reads a versionless certificate secret through `HoneyDrunk.Vault` / `ISecretStore` from a dedicated workforce Key Vault. `ClientCertificateCredential` then requests Graph tokens from the explicitly configured **customer tenant and customer app**. This is ordinary app certificate authentication; no cross-tenant MI impersonation is assumed. The certificate must contain an exportable private key in base64 PKCS#12 with an empty password. It is loaded ephemerally, never put in an environment variable or written by the API. Provider/parser errors are redacted and fail closed.

No certificate is generated or transferred by this change. First inspect the existing app and certificate metadata with approved access, then approve reuse/secure import or a replacement and public-key registration. Grant only Graph **application `User.Read.All`** for initial account lookup. Deletion/revocation permissions and admin consent are separate lifecycle decisions. Never grant Graph roles to the workforce runtime MI for customer-directory access.

Rotation: register the new public certificate in the customer app first, then create the new version under the same approved Key Vault certificate-secret name. The shared Vault cache bounds refresh; a changed secret version rebuilds the token credential. Validate convergence and token acquisition in the approved development app before retiring the old public key. Current validity and a private key are required; no secret-value fallback exists. Live rotation and tenant token exchange still require acceptance testing.

References: [MI federation prerequisites](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity), [customer-tenant supported features](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-supported-features-customers), [customer-account management](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-manage-customer-accounts).

## Selected development hosting, network and cost

On October 9, 2026 (America/New_York), the user selected **Linux B1 App Service**
with regional VNet integration and a shared SQL server with a separate Basic
Identity database. This replaces the earlier Container Apps proposal; it does
not authorize provisioning, spending, access, merge or deployment. Pulse stays
on its existing healthy Container App and is not modified.

The companion [Infrastructure PR #13](https://github.com/HoneyDrunkStudios/HoneyDrunk.Infrastructure/pull/13)
owns all Bicep. Its [connectivity plan](https://github.com/HoneyDrunkStudios/HoneyDrunk.Infrastructure/blob/feat/identity-dev/platform/sql/connectivity.md)
is authoritative for topology, named resources, regional pricing and approval scopes:

- `platform/app-network`: proposed `vnet-hd-apps-dev` and dedicated
  `snet-app-service`, delegated to `Microsoft.Web/serverFarms`, with classic
  `Microsoft.Sql` service endpoint. Explicit nonoverlapping CIDRs are still required;
  /26 is recommended. No NAT, load balancer, public IP or private endpoint is added.
- `platform/sql`: proposed `sql-hd-shared-dev` in existing `rg-hd-platform-dev`,
  East US 2. It owns Entra-only administrator, empty-by-default public firewall
  and the exact App Service subnet rule. SQL service endpoints use the public SQL
  FQDN with network rules; this is not Private Link or a private-only database.
- Identity: dedicated `asp-hd-identity-dev` Linux B1 plan, `app-hd-identity-dev`
  Web App and dedicated vault in `rg-hd-identity-dev`; Basic 5-DTU/2-GiB
  `sqldb-hd-identity-dev` on the shared server. No Pocket Quests database is created.
  Database-contained users separately enforce database authorization.
- Reuse existing ACR and logging resources. No new broker is required for sign-in.
  Shared resources and Identity resources retain their documented six ownership tags.

The reviewed East US 2 baseline is **$17.31/month**: B1 $0.017/hour × 730 = $12.41,
SQL Basic $0.161/day × 730/24 ≈ $4.90. This excludes shared ACR/Vault/logging usage,
bandwidth, additional backup/security features, taxes and other extras; it is not
an approved budget or the total Azure bill. B1 remains billed when the site is
stopped; Basic SQL does not auto-pause. Recheck prices and capacity before apply.
[App Service pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/linux/),
[SQL pricing](https://azure.microsoft.com/en-us/pricing/details/azure-sql-database/single/).

The same non-root .NET 10 Docker image runs on App Service. Always On, SQL-aware
warm-up/Health Check and managed-identity ACR pulls are configured. **B1 has no
slots**: image and configuration updates can interrupt service. A single instance
has no health-based failover. Future production sizing, redundancy and deployment
strategy require a separate review; there is only a dev dispatch path.

Initial schema review is from an approved workstation /32 with workforce Entra/MFA
and inspection-only SQL access. The administrator group, operator IP and exact
CIDRs remain unselected. No `0.0.0.0` Azure-services bypass, broad GitHub IP ranges
or Pulse IP reuse. Later GitHub Team Azure private networking can use its own
`GitHub.Network/networkSettings`-delegated subnet with a SQL endpoint/rule and
separately approved runner costs/access. It cannot share App Service's subnet.
Assigned static-IP larger runners require Enterprise Cloud; no upgrade is proposed.

## Exact access decisions held for approval

Use separate workload identities and protected GitHub environments for later automation. Initial operator planning uses an approved workforce contained user/group with MFA and the same inspection-only SQL grants; any later operator execution needs separately approved database-scoped publisher access. The logical-server administrator group is for bootstrap, not routine planning. Exact operator principals are unresolved. All app creation, OIDC federation, role assignments, SQL users and grants below are proposed only.

| Identity / environment | Proposed scope and permissions |
|---|---|
| Infrastructure principal | Existing platform access does not authorize deployment. Identity node needs its resource-group rights and scoped shared-server/database deployment rights because its DB is in the platform group. A dedicated database-only principal needs server read, database/retention write and ARM deployment rights, not server administrator/firewall writes. App setup additionally needs read/join at the exact App Service integration subnet; SQL subnet-rule setup needs joinViaServiceEndpoint/action. Pulse's environment grant is not reusable. No new User Access Administrator role is needed; these leaves create no role assignments |
| Runtime system MI | `AcrPull` on shared ACR; Key Vault Secrets User on the dedicated Identity vault (only approved certificate material there); SQL contained runtime user below. No control-plane write or SQL DDL |
| App Service CD / `dev` | Dedicated workforce OIDC app, GitHub subject `repo:HoneyDrunkStudios/HoneyDrunk.Identity:environment:dev`, audience `api://AzureADTokenExchange`; `AcrPush` on shared ACR and only the reviewed Web App/config read and image-update rights on the Identity site. No plan/network/Pulse writes, secret reads, access-admin role or SQL grant. First app initialization belongs to separately approved IaC |
| Schema planner / `dev-schema-plan` | Separate workforce OIDC app with corresponding environment subject; contained SQL user with CONNECT and VIEW DEFINITION plus the read access needed for DacFx data-loss checks. Start with SELECT on the current application tables; validate a live Script/DeployReport before accepting this scope. No DDL |
| Schema publisher / `dev-schema-publish` | Separate workforce OIDC app with corresponding subject; database-scoped `db_owner` on **only** `sqldb-hd-identity-dev` for DacFx. DacFx may alter schemas/constraints/postdeployment data, so `db_ddladmin` alone is not assumed sufficient. No SQL server admin or Azure resource role. Required reviewer and branch policy on environment; consider time-bounded membership |
| SQL bootstrap administrator | Approved workforce Entra **group**, explicit object ID and group name, as logical-server Entra-only admin. Human-reviewed contained-user/grant setup; no SQL password |
| Customer Graph app | Verify existing customer app. Register/reuse approved certificate, application User.Read.All and customer-tenant admin consent; verify API scope, delegated consent, user flow and exact redirect URIs independently |

Configure environment reviewers (prevent self-review where available) and main-only branch policies **before** enabling `APP_SERVICE_DEPLOYMENT_APPROVED=true` or `IDENTITY_SCHEMA_APPROVED=true`. The workflow checks required reviewers for the write environments and refuses absent setup. Environment changes themselves are held for review. Leave `IDENTITY_SQL_RUNNER` unset for the initial operator path; later it identifies the approved Linux larger runner using the separately accepted private-network path. In each later schema environment configure `IDENTITY_SQL_SERVER` and its own `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`; **no subscription ID or ARM role is required for these SQL-only identities**. Both `azure/login@v2` steps set `allow-no-subscriptions: true` and omit `subscription-id`. The [action implementation at 7184910](https://github.com/Azure/login/blob/7184910d9eb2b1c5e48f7073824a90609bb9b6d6/src/Cli/AzureCliLogin.ts) adds `--allow-no-subscriptions` to the OIDC login and calls subscription selection only when a subscription ID is supplied. The app CD `dev` environment also needs `APP_SERVICE_NAME` and its separate `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` settings. The shared workflow resolves these in the caller's protected dev environment. No client secret is required for these OIDC apps.

### SQL contained users and runtime boundary

Do not put users or grants in the DACPAC. Azure SQL supports `CREATE USER ... WITH SID=..., TYPE=E` without a Graph lookup; for **service principals and managed identities the SID is the application/client ID**, not the service principal object/principal ID. Resolve and validate each application ID in the workforce tenant first. This avoids granting the SQL server Directory Readers solely for user creation. Entra groups use their object ID and `TYPE=X`. See [CREATE USER, example K](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-user-transact-sql?view=azuresqldb-current#k-create-a-contained-database-user-from-a-microsoft-entra-principal-without-validation).

An approved SQL administrator can derive the binary SID with `CONVERT(binary(16), CONVERT(uniqueidentifier, '<verified-client-id>'))`, copy its `0x...` literal into `CREATE USER [identity-runtime] WITH SID=0x..., TYPE=E`, and grant CONNECT. Proposed runtime role `identity_runtime` receives SELECT/INSERT/UPDATE/DELETE on the exact current tables `dbo.Users`, `dbo.Subjects`, `dbo.AuditRecords`, `dbo.Deliveries`, `dbo.Erasures`, `outbox.OutboxMessages`. Historical `dbo.Audit` is preserved, not part of current runtime queries. Audit DELETE is required by the existing retention/erasure implementation; it does not imply general schema rights. No ALTER, CREATE, CONTROL, db_owner, db_ddladmin or grant authority. Review finer per-table verbs against future code changes. Prove SELECT/write success and CREATE/ALTER/DROP denial under the runtime token before serving traffic.

## Schema review and deployment order

The default SQL Server 150 DACPAC remains for local development. Cloud validation compiles the same schema with `Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider` into `bin/AzureSql`; this catches platform differences without `AllowIncompatiblePlatform`. There is no startup schema creation or migration. Successful compilation is not a live migration/recovery rehearsal.

1. Review and merge only with explicit approval, in dependency order: Actions #218,
   Identity foundation #1 (currently Sonar-blocked), then the rebased Identity #3
   and Infrastructure #13. The consumer pins the exact Actions commit; no mutable
   workflow reference is used for this release path.
2. Approve names, costs, CIDRs and exact principals/permissions. Review isolated
   network and SQL plans, then separately approve apply. Do not replay broad
   platform infrastructure or modify Pulse. Add only the exact approved SQL subnet
   rule after its endpoint exists. Inspect the full firewall set because incremental
   ARM does not delete omitted old rules.
3. Review Identity bootstrap/database/vault creation. Bootstrap creates the
   system MI on a **disabled placeholder Web App**, not a healthy serving API.
   It creates no role assignments. Resolve the runtime MI client ID and obtain
   separate approved AcrPull, vault-read and contained SQL DML grants.
4. Build the Azure SQL DACPAC at the reviewed revision. Use the approved operator
   session and `scripts/Review-DevDatabase.ps1 -Action Plan` with the exact shared
   SQL FQDN, database, package, source revision and fresh review directory. Review
   `deploy.sql`, `deploy-report.xml`, `manifest.json` and hashes together. Tokens
   stay in memory; never publish them or printed credential commands.
5. **Stop before SQL execution.** `Publish` deliberately fails even after fresh
   Script/DeployReport comparisons pass. The all-DDL-writer maintenance freeze is
   still unapproved; exact-script execution with target-state protection and real
   database tests remains unimplemented. Manual SSMS/SqlPackage Publish is not a
   bypass. Do not enable execution by changing a workflow variable.
6. Once schema, customer credentials/configuration and live approval gates are
   resolved, build/scan a reviewed image with a unique `RELEASE_ID` and retain its
   ACR digest. Apply complete reviewed `appUpdate` through Infrastructure to
   initialize and enable the site. Never override image-baked `Identity__ReleaseId`
   with an app setting. No startup migration runs.
7. Later image releases use the shared development App Service workflow from
   Actions and this repo's `scripts/deploy_app_service.py`. Required environment
   reviewers, explicit enablement and main-only execution are checked. Build/scan
   precedes push; retained rollback images are scanned again before deployment.
   The verifier saves the previous digest and observed release before a write,
   updates only the serving image, reads configuration back, and requires three
   consecutive HTTP 200 responses from **both** `/health/live` and `/health` with
   the expected `X-Identity-Release`. An old healthy image cannot satisfy that test.
8. Inspect release evidence and perform authenticated `/users/me`, authorized
   DML, denied DDL/account-isolation, telemetry and restart acceptance. Health
   success alone does not prove any of these. No live acceptance has run.

### Rollback and failure handling

A failed update stays failed/unknown and retains evidence. It does not claim an
automatic rollback. Review the actual state, then explicitly select `rollback`
with the previously retained immutable digest and its baked release ID. The same
image-update and release/readiness checks apply; B1 offers no slot swap or isolated
zero-traffic candidate. Preserve known-good images and release artifacts.

Image rollback does not undo SQL, app settings, certificate registrations or
consent. Serialize IaC, schema operations and CD across repositories. Database
recovery needs a separately reviewed PITR drill with erasure replay/reconciliation;
a restored database must not resurrect deleted accounts. Production lifecycle,
restore and rollback acceptance remain unproven.

## Lifecycle and release blockers

Basic sign-in needs SQL, the customer Graph read credential and client authentication configuration. Service Bus is unnecessary for this first milestone. Optional Infrastructure queues `identity-lifecycle-acks` and `pocketquests-lifecycle` are source preparation only; no topic is needed for the current per-consumer queue contract, and no consumer is implemented by this PR.

Before enabling deletion/recovery: implement and validate each product's idempotent consumer, private ack transport, scoped Service Bus Sender/Receiver grants, duplicate/out-of-order handling, dead-letter operation, authenticated account proofs, Graph deletion/session-revocation permissions, recovery reconciliation and erasure-ledger preservation beyond all relevant backup windows. Then configure consumers/namespace and enable delivery deliberately. Queue provisioning alone does not meet these gates. The SQL backup policy and the in-database erasure ledger do not yet prove safe restore.

Foundation [PR #1](https://github.com/HoneyDrunkStudios/HoneyDrunk.Identity/pull/1) at `f95491fd0715cb268ec68cdd7d92aca706b61a48` is blocked: organization ruleset `16846333` requires exact status **SonarCloud Code Analysis**, which is absent even though the Sonar workflow succeeded ([run](https://github.com/HoneyDrunkStudios/HoneyDrunk.Identity/actions/runs/37220987779)). Existing analysis logs also show S8969/S3776/S1075/S6667 warnings; exact new-code issue status could not be retrieved from the public project API. Repair/check the Sonar integration and actual quality gate; do not remove the required check or treat workflow success as zero issues. This implementation is a dependent PR against `feat/identity-foundation`; rebase/retarget after the foundation merges with approval.

Proposed Studio status text for its owning task: "Identity dev deployment implementation is in review. Shared Azure resources and separate customer-tenant topology were verified read-only. SQL publication is disabled pending exact-script execution with protection against concurrent DDL. Customer app access, SQL network/identity setup, cost approval, live credential/sign-in/restore validation and the foundation Sonar status remain gates. Nothing has been provisioned or deployed; lifecycle production readiness is not claimed." No Studio file is changed here.

