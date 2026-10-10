# Changelog

## [0.1.0-alpha.4] - Unreleased candidate

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

