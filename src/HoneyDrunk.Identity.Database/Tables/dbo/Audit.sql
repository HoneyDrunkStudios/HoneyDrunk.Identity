CREATE TABLE [dbo].[Audit] (
    [Id] bigint NOT NULL IDENTITY,
    [OccurredAt] datetimeoffset NOT NULL,
    [EventName] nvarchar(100) NOT NULL,
    [Outcome] nvarchar(40) NOT NULL,
    [ActorHash] varchar(64) NOT NULL,
    CONSTRAINT [PK_Audit] PRIMARY KEY ([Id])
);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'One preserved prototype Identity audit event. Restricted pseudonymous personal data. Historical rows only; new events use the shared AuditRecords table. OccurredAt is the event stamp and each row is history. No new writes grow this table; matching rows are removed on account erasure, with no separate age-based pruning implemented.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Audit';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Original database-generated numeric identifier of the preserved prototype audit event.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Audit', @level2type = N'COLUMN', @level2name = N'Id';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC instant of the original prototype event; preserved during migration.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Audit', @level2type = N'COLUMN', @level2name = N'OccurredAt';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Original prototype event name; retained verbatim rather than rewritten into the shared audit vocabulary.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Audit', @level2type = N'COLUMN', @level2name = N'EventName';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Original prototype outcome text; historical values are preserved without a new enumeration constraint.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Audit', @level2type = N'COLUMN', @level2name = N'Outcome';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Pseudonymous hexadecimal SHA-256 actor digest from the prototype; erasure matches known subject/account digests. It is personal data, not an anonymization guarantee.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Audit', @level2type = N'COLUMN', @level2name = N'ActorHash';
GO
