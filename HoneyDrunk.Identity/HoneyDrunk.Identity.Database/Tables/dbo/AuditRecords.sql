CREATE TABLE [dbo].[AuditRecords] (
    [Id] varchar(32) NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [Actor] nvarchar(max) NOT NULL,
    [EventName] nvarchar(200) NOT NULL,
    [Category] int NOT NULL,
    [Outcome] int NOT NULL,
    [TargetType] nvarchar(max) NOT NULL,
    [TargetId] nvarchar(max) NOT NULL,
    [TargetDisplayName] nvarchar(max) NULL,
    [TenantId] nvarchar(100) NOT NULL,
    [CorrelationId] nvarchar(max) NULL,
    [Operation] int NOT NULL,
    [Reason] nvarchar(max) NULL,
    [ChangesJson] nvarchar(max) NOT NULL,
    [MetadataJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_AuditRecords] PRIMARY KEY ([Id])
);
GO

CREATE INDEX [IX_AuditRecords_TenantId_OccurredAt] ON [dbo].[AuditRecords] ([TenantId], [OccurredAt]);
GO
