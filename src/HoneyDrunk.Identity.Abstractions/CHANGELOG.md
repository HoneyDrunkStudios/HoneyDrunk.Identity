# Changelog

## [0.1.0-alpha.5] - 2026-11-05

### Changed

- Refresh NuGet dependencies and adopt upstream package releases in dependency order. Preserve target frameworks and public contracts.

| Dependency | Previous | Updated |
| --- | --- | --- |
| HoneyDrunk.Audit.Abstractions | 0.2.1 | 0.2.2 |
| HoneyDrunk.Audit.Data | 0.2.1 | 0.2.2 |
| HoneyDrunk.Auth | 0.6.1 | 0.6.2 |
| HoneyDrunk.Data.EntityFramework | 0.7.1 | 0.7.2 |
| HoneyDrunk.Data.Outbox | 0.7.1 | 0.7.2 |
| HoneyDrunk.Data.Outbox.Dispatcher | 0.7.1 | 0.7.2 |
| HoneyDrunk.Kernel | 0.8.1 | 0.8.2 |
| HoneyDrunk.Standards | 0.3.0 | 0.4.3 |
| HoneyDrunk.Telemetry.OpenTelemetry | 0.4.1 | 0.4.2 |
| HoneyDrunk.Transport.AzureServiceBus | 0.7.2 | 0.7.3 |
| HoneyDrunk.Vault.Providers.AzureKeyVault | 0.8.1 | 0.8.2 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 | 10.0.14 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | 10.0.14 |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.12 | 10.0.14 |
| Microsoft.NET.Test.Sdk | 17.14.1 | 18.11.0 |
| System.Security.Cryptography.Pkcs | 10.0.13 | 10.0.14 |
| Testcontainers.MsSql | 4.15.0 | 4.16.0 |
| coverlet.collector | 6.0.4 | 10.1.0 |
| xunit.runner.visualstudio | 3.1.4 | 4.0.1 |

## [0.1.0-alpha.4] - Unreleased local candidate

- Organize contracts into Accounts, Authentication, and AccountLifecycle folders and matching namespaces; source consumers must update their imports.
- Add explicit account lifecycle, recent-authentication and provider linking contracts.
- Preserve canonical issuer/subject identity while carrying verified provider object metadata.

## [0.1.0-alpha.3] - Unreleased candidate

- Define the verified external-subject, stable account and user-directory contracts implemented by Identity.
- Include repository, license and package-readme metadata. No placeholder lifecycle contracts are published.
