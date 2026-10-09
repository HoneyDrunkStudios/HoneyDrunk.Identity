# Changelog

## [0.1.0-alpha.4] - Unreleased candidate

- Prepare dev Container App packaging, zero-traffic candidate deployment, explicit CORS/OTLP configuration and schema-aware health probes.
- Add an Azure SQL build target and a Script/DeployReport review workflow with separate protected publication, artifact verification and drift checks. Startup never applies schema.
- Require explicit deployed Graph credentials; resolve the external-tenant app certificate through HoneyDrunk.Vault with ephemeral private-key loading and version refresh.
- Document verified dev inventory, tenant/network unknowns, cost assumptions, permission approvals, rollback and outstanding lifecycle/recovery gates. No live deployment is performed.
- Add account lifecycle contracts, feature namespaces and SQL Server database project deployment.
- Require delegated API scope and support certificate-based local Entra integration.
- Simplify lifecycle cleanup and delivery filtering, fix the test DACPAC filename, and verify caller disposal of Graph test responses.
- Require manual lifecycle acknowledgment settlement and reject automatic-completion/Blob-fallback overrides. Keep registered lifecycle publishers broker-only so failed sends remain retryable in the SQL outbox; add local composition regression tests.
- Retry EF write failures without stopping lifecycle maintenance, and keep retention cleanup scheduled when messaging is disabled while leaving lifecycle delivery disabled.


## [0.1.0-alpha.3] - Unreleased candidate

- Add stable external-subject/account contracts and an independent Identity HTTP client.
- Add the SQL-backed service with shared Auth, Kernel, Data, Audit and Pulse integration.
- Validate packages in an isolated consumer and use shared Actions for review and release.
- This candidate has not been published. Provider enrollment and production lifecycle work remain incomplete.
