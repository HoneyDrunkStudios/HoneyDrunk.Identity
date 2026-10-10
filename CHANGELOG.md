# Changelog

## [0.1.0-alpha.4] - Unreleased candidate

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
