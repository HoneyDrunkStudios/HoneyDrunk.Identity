# Changelog

## [0.1.0-alpha.4] - Unreleased candidate

- Require an independently approved database-owner SID and reject altered built-in role/schema ownership before first initialization, with real disposable SQL regressions.

- Refuse Azure SQL database-level firewall rules during first initialization, recheck between execution batches and roll back on detected access drift; document the administrator firewall freeze and detection limits.

- Route Identity traces, metrics and logs to distinct Pulse HTTP/protobuf endpoints with an actual transport regression test.
- Add the bounded empty-dev database initializer used for the first Central US release, with exact-artifact checks, a DDL maintenance guard, atomic execution and reproducible LocalDB rollback/competing-writer tests in CI. The general schema publisher remains disabled.
- Document the deployed runtime, managed-identity permissions, certificate rotation and remaining recovery/release-automation work.

- Reconcile lifecycle deliveries with the current consumer registry before transitions and retries, retire removed destinations, and renew capabilities after route changes without touching another account's outbox.
- Restrict deployment CLI operations and evidence output to the reviewed dev target/file; import executed Python and offline DacFx/Pester script coverage alongside .NET coverage without changing Sonar gates or enabling SQL publication.
- Refresh the PR validation pin to the current reviewed shared workflows while retaining strict formatting.
- Align deployment preparation with merged Terraform ownership and pin the shared App Service workflow with its required read-only Actions permission; preserve SQL execution and runtime-initialization holds.
- Correct the GitHub Team/static-IP assumption; recommend initial operator-run schema review, revise the Azure subtotal and document later Team private-network automation without changing execution holds.
- Align the deployment review and offline schema fixture with the approved shared-server/Basic design; document costed network/operator/runner choices while preserving the SQL execution hold.
- Replace the unshipped dev Container Apps proposal with Linux B1 App Service, VNet SQL service-endpoint integration and direct immutable-image deployment. Verify baked release identity on both health paths and retain explicit image-rollback evidence; no slots or zero-downtime claim.
- Move release orchestration to the shared Actions workflow while retaining Identity-specific validation. Keep provisioning, permissions and SQL execution held.
- Add an Azure SQL build target and a Script/DeployReport review workflow with separate protected revalidation, artifact verification and drift checks. Startup never applies schema.
- Revalidate actual generated SQL as well as DeployReport; reproduce equal reports with different ALTER COLUMN SQL using real DacFx models. Disable schema execution until the exact-script/concurrent-DDL boundary is reviewed.
- Use tenant-only OIDC for SQL identities without subscription selection or ARM grants; verify formatting against the nested solution and correct two whitespace findings.
- Pin the shared formatter correction and enable strict CI formatting of the nested solution; workspace/invocation errors no longer become successful warning-only checks.
- Require explicit deployed Graph credentials; resolve the external-tenant app certificate through HoneyDrunk.Vault with ephemeral private-key loading and version refresh.
- Document verified dev inventory, tenant/network unknowns, cost assumptions, permission approvals, rollback and outstanding lifecycle/recovery gates. No live deployment is performed.
- Verify consumer lifecycle HTTP routes, owner proofs, confirmation payloads, missing status and authentication-versus-availability failures; use a standard redacted exception snapshot with safe method frames.
- Configure the Graph base URI with a strict HTTPS Microsoft-host/API-path boundary and no redirects; preserve failure diagnostics through a bounded exception snapshot that removes raw messages, data, database entries and file paths before logging.
- Describe the five Identity-owned SQL tables and all columns, including classification, lifecycle and retention, and name their four existing default constraints without changing the domain model or shared Audit/Outbox tables.
- Use the SQL Server Sonar analyzer and actual new-code checks, simplify session revocation and erasure completion without changing transaction boundaries, and resolve test/analyzer diagnostics and object-initializer formatting.
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
