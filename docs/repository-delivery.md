# Repository delivery and review

## Scope

The merged Identity foundation includes responsibility-based project organization, account lifecycle contracts, SQL Server database/DACPAC deployment, local Entra credential support and Visual Studio SSDT compatibility. Application projects remain .NET 10. The two public package candidates are Client and Abstractions, version 0.1.0-alpha.4; no package release is established by its merge.

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

Deployment PR validation measures the offline Python deployment tests with coverage.py 7.11.0 and the real DacFx schema-review fixture with Pester 5.7.1. Azure/SQL transport remains mocked and the SQL publisher stays disabled. The PR workflow converts the producers' actual Cobertura line/branch counts into portable Sonar generic coverage and combines it with the existing .NET OpenCover artifact. Missing reports, unresolved source paths and overlapping coverage lines fail instead of inventing hits. The same script measurements run on main before analysis; the shared scanner collects .NET fallback coverage there. No SQL source coverage is synthesized and no Sonar threshold or coverage/source exclusion was changed. The separately approved duplication-only scope is recorded below.

For local Python verification, install `scripts/coverage-requirements.txt` in an isolated environment and run `python -m coverage run --branch --source=scripts -m unittest discover -s scripts/tests -v`, then `python -m coverage xml -o TestResults/python.xml`. The PowerShell configuration in `.github/workflows/pr.yml` runs `scripts/tests/DatabaseReview.Tests.ps1` and writes `TestResults/powershell.xml`; it requires restored SqlPackage tools and Pester 5.7.1. Convert both reports with `python scripts/convert_script_coverage.py --output TestResults/script-coverage.xml TestResults/python.xml TestResults/powershell.xml`. The deployment helper accepts only `app-hd-identity-dev`, a reviewed registry digest, and the fixed `deployment-evidence/release.json` evidence path.

## Approved duplication scope

On October 10, 2026 the owner approved `sonar.cpd.exclusions` for exactly these files under `HoneyDrunk.Identity/HoneyDrunk.Identity.Database/Tables/dbo/`: `Audit.sql`, `Deliveries.sql`, `Erasures.sql`, `Subjects.sql` and `Users.sql`. No wildcard or source/coverage exclusion was added. Descriptions stay beside their tables and in the DACPAC model. The 3% duplication threshold elsewhere, SQL issue analysis, coverage and strict new-code checks remain. Future DDL duplication within these five files is outside CPD coverage and needs manual review; any scope change requires separate approval.

The final foundation [validation run](https://github.com/HoneyDrunkStudios/HoneyDrunk.Identity/actions/runs/38087710688) on `0baf16f` passed all 83 hosted .NET tests and actual script measurements, completed native review and passed real/strict Sonar after this approval: zero new/open findings, 85% new-code coverage and 0% measured duplication. The foundation merged to main as `e4f22c7`; later revisions still require their own checks. Main-branch baseline analysis and environment verification are distinct from this final PR evidence.

## Release gates

The founder separately authorized the source merge. No release tag, NuGet publication or production deployment follows from that authorization. Live authenticated product flows after the local schema upgrade, native devices, production transport/provider erasure, recovery/retention and operational monitoring remain environment-specific gates. Local credentials, database backups and package artifacts are ignored and excluded from publication.
