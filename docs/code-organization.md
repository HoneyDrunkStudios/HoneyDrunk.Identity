# Code organization

Open `HoneyDrunk.Identity/HoneyDrunk.Identity.slnx`. Projects sit beside the solution; each project groups implementation files by responsibility.

| Project | Folders and responsibility |
| --- | --- |
| HoneyDrunk.Identity | `Accounts` for directory operations; `AccountLifecycle` for recovery, linking, and erasure; `Auditing` for audit integration; `Persistence` for SQL access |
| HoneyDrunk.Identity/Persistence | `Context` for EF access; `Entities` for database records; `Configurations` for Fluent API mappings |
| HoneyDrunk.Identity.Database | `Tables` for SQL definitions; `Schemas` for SQL schemas; `Data/Seed` and `Data/AdHoc` for data scripts; `PublishProfiles` for DACPAC targets |
| HoneyDrunk.Identity.Abstractions | `Accounts`, `Authentication`, and `AccountLifecycle` for provider-independent contracts |
| HoneyDrunk.Identity.Api | `AccountLifecycle`, including `Endpoints` and `Messaging`; application bootstrap remains in `Program.cs` |
| HoneyDrunk.Identity.Providers.Entra | `Accounts` for Graph operations, `Authentication` for bearer validation and signing keys, `Configuration` for registration |
| HoneyDrunk.Identity.Tests | `Accounts`, `AccountLifecycle`, `Providers`, and reusable `Fixtures` |
| HoneyDrunk.Identity.Client | The single HTTP client remains at the project root |

Persistence types end in `Entity`, such as `UserEntity`. Consumer-facing account contracts, such as `UserRecord`, have no persistence suffix. Each table mapping has its own `IEntityTypeConfiguration<T>` implementation; the context discovers those configurations from its assembly. The shared Audit and Outbox packages retain their own record types.

Namespaces follow folders. Pocket Quests source references have been updated with this refactor. This is a source-level change to the unreleased Identity contracts; consumers using an older packaged candidate need a matching updated package before adopting the new namespaces.

The SQL project owns schema deployment and preserves existing table names. Integration tests deploy its DACPAC instead of EF migrations. See [database project](database-project.md) for publishing and data-script conventions.
