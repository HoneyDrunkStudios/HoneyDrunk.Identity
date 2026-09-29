# Repository delivery and review

## Scope

The existing Identity foundation PR now includes responsibility-based project organization, account lifecycle contracts, SQL Server database/DACPAC deployment, local Entra credential support and Visual Studio SSDT compatibility. Application projects remain .NET 10. The two public package candidates are Client and Abstractions, version 0.1.0-alpha.4; no package release is part of this PR.

## Explicit review

Reviewed authentication and account ownership, secondary link proofs, transaction/lock boundaries, audit and erasure behavior, lifecycle acknowledgments, SQL schema deployment, package consumers, solution portability, workflow paths and source-file hygiene.

Publication-review fixes:

- Require the exact delegated `access_as_user` scope on authenticated requests and additional account-link proofs. Tests reject absent and lookalike scopes without creating accounts.
- Remove the hard-coded social-provider list from public configuration; configured Entra user flows own that UI.
- Update the package-consumer fixture to the reorganized Accounts namespace.
- Preserve SQL tooling compatibility with native SSDT and CLI Microsoft.Build.Sql while keeping application runtime targets unchanged.

Earlier review corrected scoped Auth lifetimes, canonical audit atomicity, forged public context headers, SQL concurrency and portable Linux/Windows test fixtures. No migration coverage exclusion remains: schema is owned by the SQL project.

## Validation

Local validation on September 28 passed 33 full-suite tests plus the additional link/unlink scope regression (34 cases). Release builds use shared Standards with warnings treated as errors. SQL boundary tests deploy the actual DACPAC to isolated databases. A separate package consumer restores fresh and locked into a new cache and builds without source references. Workflow syntax is checked with actionlint. Native Visual Studio SQL and CLI DACPAC models were compared with no schema differences.

GitHub results are specific to the PR commit and must be read from its checks; previous passing runs do not prove the current revision. The required SonarCloud external check previously needed organization-owner app access/project binding. Keep the required gate intact and verify current status before merging.

## Release gates

No merge, release tag, NuGet publication or production deployment is authorized by this publication. Live authenticated product flows after the local schema upgrade, native devices, production transport/provider erasure, recovery/retention and operational monitoring remain environment-specific gates. Local credentials, database backups and package artifacts are ignored and excluded from publication.
