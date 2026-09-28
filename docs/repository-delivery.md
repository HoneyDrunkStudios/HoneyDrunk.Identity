# Repository delivery and review

September 28, 2026. This is a reviewed implementation foundation, not a deployed identity platform or a completed provider enrollment.

## Established node conventions

The public repository matches Auth, Data and Audit visibility. A minimal `chore: bootstrap identity repository` commit contains only the existing studio MIT license and ignore rules. Implementation is reviewed on `feat/identity-foundation`; no merge or release was authorized.

Workflow callers reuse HoneyDrunk.Actions at `main`, matching maintained node callers. The reviewed Actions revision was `637bd915cbce1f2ca34152dfcdca266725db97b4`. PR Core runs build/tests, Standards/static analysis, secret scan, dependency scan, CodeQL, metadata and coverage gates. SonarCloud runs through the shared job. The main-branch coverage ratchet, Grid review request, weekly dependencies and nightly security callers follow Auth/Audit conventions. No shared Actions changes were needed.

Package Validation packs only Client and Abstractions and compiles a consumer in a fresh temporary directory with a fresh NuGet cache and no source project references. Linux CI starts real SQL Server through Testcontainers; Windows tests use MSSQLLocalDB. Missing SQL fails tests rather than skipping them. Generated EF migration files are excluded from coverage only; authored service code remains measured.

The existing organization `main` and `SonarCloud Quality Gate` rulesets now include Identity. Their existing rules and bypass actors were preserved. GitHub Actions remains enabled with read-only default token permissions and no permission for Actions to approve PRs. The existing organization SONAR_TOKEN selected-repository binding includes Identity; no token was read or rotated.

GitHub rejected adding Identity to the SonarQube Cloud GitHub App's selected repositories with HTTP 403 (organization-owner permission required). The owner must add this repository to that existing installation and complete Sonar project onboarding. The Sonar check remains required. Do not remove the gate or merge around it. This access step is tracked without a deadline in Architecture's canonical manual-action ledger. The scanner job has uploaded analysis successfully; the required SonarCloud Code Analysis GitHub check is still absent.

## Explicit code and architecture review

Review covered trust boundaries, issuer/subject isolation, SQL concurrency, transaction rollback, canonical audit preservation, DI lifetimes, package boundaries, portable test execution, workflows, release gating and tracked-file hygiene.

Findings fixed during explicit review:

- Scoped Auth dependencies no longer sit under a singleton validator; Kernel context/telemetry runs before authentication.
- Full canonical Audit.Data records replace a lossy adapter and append-time purge. Original prototype records remain readable. User creation and its audit event commit atomically; an injected audit failure leaves no user or mapping.
- Public Grid headers cannot assign tenant or correlation ownership. Cryptographic validation precedes directory resolution; issuer, audience, expiry, missing subject, invalid signature and malformed tokens are covered.
- SQL tests no longer depend on a custom PocketQuests LocalDB instance in CI. Linux uses isolated SQL Server containers and Windows uses the standard automatic LocalDB instance.
- Sonar findings corrected: await host shutdown asynchronously, place the test assembly marker in a named namespace, match the model-builder parameter name, and keep authority as a constructor-local value.
- Explicit PackageId values and versioned package/repository changelogs ensure the shared NuGet version gate actually examines the intended packages.
- Only the implemented Client and Abstractions are packable. Service internals cannot accidentally enter the public NuGet release. Package metadata changes advanced the local candidate through internal alpha.1/alpha.2 candidates to **0.1.0-alpha.3**.
- Manual releases must run from main; release commits must already belong to main. The shared release workflow runs tests/security checks before publishing and marks prereleases appropriately. No release workflow was dispatched.

Local evidence: 14 SQL/authentication/boundary tests pass; line coverage is 86.9% (166/191 authored executable lines); Release build has zero warnings/errors; an isolated alpha.3 package consumer passes fresh and locked restore plus build. The solution pack command emits the expected informational warning that the API is not packable. Workflow syntax is checked with actionlint. GitHub-hosted Linux SQL tests, coverage gate, CodeQL (no findings), secret/dependency scans and isolated package consumer passed on the first PR run. The Sonar scan job succeeded, but that is not the required external quality-gate check.

## Release and consumer gates

Publish uses the same shared `release.yml` and NuGet.org target as Auth, with the existing organization NUGET_API_KEY. Do not issue a version tag or dispatch Publish until review/protections pass, ADR/catalog ownership is reconciled, package metadata/version is approved, and release authority is given. Packages are immutable; changes require a new version. Service deployment and schema rollout are separate from client-package publication.

Pocket Quests currently pins `[0.1.0-alpha.3]` from an ignored local candidate feed, with lockfiles and explicit optional Identity source configuration for integration tests/Aspire. It has not consumed a published package. After an approved NuGet release, update its Identity source mapping to NuGet.org and verify a clean restore without `.packages/feed`, then lock the released package contents. Do not claim ordinary remote restore works before that proof.

Provider setup, billing consent, lifecycle expansion, account linking/recovery, erasure, production retention/alerts and native-device validation remain the separate gates documented in the README. No Entra tenant, Apple/Google enrollment, deployment or paid plan was created.
