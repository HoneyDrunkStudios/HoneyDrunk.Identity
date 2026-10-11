# First development runtime initialization

The approved October 11, 2026 deployment uses Central US for the Identity B1 app
and Basic SQL database, with the existing registry, logging and Identity vault
in East US 2. The earlier [deployment proposal](development-deployment.md) is
historical; its pending approvals and regional assumptions do not describe this
deployment. Existing resources were preserved. No Microsoft support ticket was
submitted.

## Database boundary

`scripts/Initialize-DevDatabase.ps1` supports only the fixed, empty development
database and requires the app to be disabled. It does not upgrade a populated
database. The general `Review-DevDatabase.ps1 -Action Publish` remains disabled.

An operator with approved workforce Entra access and a temporary single-IP SQL
firewall rule first runs `-Action Plan -Package <AzureSql.dacpac>
-ReviewDirectory <private-directory> -SourceRevision <40-character-commit>
-ApprovedOwnerSid <approved-hex-SID>`.
Review the generated SQL, DeployReport and manifest before separately authorized
`-Action Initialize` with the same arguments. It checks the package, script,
report, executor module and wrapper hashes, parses SQLCMD and SQL batches, installs a session-bound
DDL maintenance guard, compares a fresh plan, and executes the reviewed batches
inside an outer transaction. It refuses nonempty targets, unexpected users,
altered fixed-role/schema ownership and a database owner that differs from the
approved SID. Establish that SID from the approved owner identity and provisioning
record; do not accept an observed target SID without independently verifying its
owner. The manifest binds the SID, and initialization rechecks ownership under
the guard. Existing manifests without this approval must be regenerated.
The helper requires Windows x64 PowerShell 7 and uses the SqlPackage version
pinned in `dotnet-tools.json`, resolved from the configured NuGet cache. Restore
tools before use. Changing either executor file invalidates an existing plan.

To run the isolated checks, build the SQL project in Release, start
`(localdb)\MSSQLLocalDB`, and run `scripts/Test-IdentityBootstrap.ps1`. The test
generates SQL from that DACPAC, compares the fresh plan under the maintenance
guard, injects a mid-DDL failure, verifies rollback, and checks a complete commit
and competing-writer rejection. CI runs the same tests on Windows.

Other administrators must observe the maintenance window. The guard blocks
ordinary competing DDL; it is not a security boundary against a privileged
administrator deliberately disabling it. If the connection is lost and the guard
remains, inspect transaction/schema state and coordinate recovery before removing
it. Never rerun initialization against an existing schema. Local rollback and
competing-DDL tests do not establish Azure point-in-time recovery.

Freeze database firewall administration for the entire maintenance window too.
Azure SQL database-level firewall rules grant access independently of the server
rules. Initialization rejects any existing database rule before planning and under
the guard, then rechecks inside the transaction before execution and after every
batch. A detected rule aborts/rolls back schema work; catalog-read errors fail
closed. Firewall procedures are not protected by the DDL trigger: these checks
detect persistent changes, not a rule another administrator adds and removes
between checks. They do not revoke an external access change. Stop and coordinate
access recovery if a check fails. LocalDB has no such catalog; offline tests cover
the Azure-specific checks without claiming a live Azure firewall-mutation test.

The same Azure metadata checks reject every database-scoped Extended Events
session, running or stopped, before planning, under the guard and between execution
batches. Such sessions can retain tracing configuration beyond initialization.
Freeze tracing administration during maintenance as well; detection does not remove
sessions or prevent an administrator from creating and removing one between checks.
Catalog-read errors abort initialization. LocalDB supports server-scoped sessions
and lacks this Azure database-scoped catalog, so the event-session rejection,
rollback and catalog-failure regressions use offline mocks, not live Azure evidence.

## Runtime credentials and activation

Create a contained external SQL user using the app managed identity's **client
ID**, not its principal object ID. Grant only CONNECT and DML on the six current
application tables; no db_owner, DDL or historical `dbo.Audit` grant. App Service
uses managed identity for SQL, AcrPull and Key Vault secret reads.

Customer Graph authentication uses the existing customer application with
application User.Read.All. Add an approved public certificate without removing
the existing key, verify Graph token acquisition, and store the private PKCS#12
as base64 in `identity-graph-client-certificate` in the dedicated vault. Never
write it to app settings, source, logs or Terraform state. The October 11 key
expires January 9, 2027; rotate before then and retire old keys only after review.

The initial runtime operation configures the reviewed settings and immutable
ACR image, then enables the app. Subsequent image updates use the existing image
deployment contract. SQL changes and credentials are separate operations.
Terraform owns infrastructure and explicitly delegates the image, settings,
health path and enabled state to the release process.

`OTEL_EXPORTER_OTLP_ENDPOINT` is the Pulse base HTTP receiver ending in `/otlp/`.
Identity retains HoneyDrunk resource conventions and instrumentation, and uses
OpenTelemetry's cross-signal HTTP/protobuf exporter to append `v1/traces`,
`v1/metrics` and `v1/logs`. The pinned HoneyDrunk exporter assigns a literal URL
to each signal and cannot route this receiver correctly by configuration alone.
The checked-in Development endpoint uses the standard HTTP receiver at
`http://localhost:4318`; collector overrides must also select an HTTP receiver.
The transport test sends real protobuf for all three signals to a loopback
receiver; it does not claim remote dashboard retention.

## Acceptance and cleanup

Verify baked release headers on both health paths, SQL readiness, a real customer
PKCE sign-in, stable `/users/me`, account status, missing/invalid-token rejection,
and spoofed ownership-header rejection. Verify account/binding/audit persistence
and repeat after restarting the app. Never test deletion on the user's account.

Remove the temporary SQL firewall rule and operator Key Vault installation role,
recheck runtime health and Terraform drift, close the local verifier to discard
its in-memory token, and retain sanitized release evidence. Do not archive
tokens, private certificates, state, private plans or customer identity claims.
Lifecycle erasure/broker activation, product browser CORS, native-device testing,
backup restoration and automated GitHub release identities remain separate work.
