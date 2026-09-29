CREATE TABLE [outbox].[OutboxMessages] (
    [Id] uniqueidentifier NOT NULL,
    [Type] nvarchar(512) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [Headers] nvarchar(max) NULL,
    [TenantId] nvarchar(128) NULL,
    [CorrelationId] nvarchar(128) NULL,
    [Status] int NOT NULL,
    [RetryCount] int NOT NULL DEFAULT 0,
    [NextAttemptAt] datetimeoffset NULL,
    [LeasedUntil] datetimeoffset NULL,
    [LastError] nvarchar(max) NULL,
    CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
);
GO

CREATE INDEX [IX_OutboxMessages_CorrelationId] ON [outbox].[OutboxMessages] ([CorrelationId]);
GO

CREATE INDEX [IX_OutboxMessages_Status_NextAttemptAt_OccurredAt] ON [outbox].[OutboxMessages] ([Status], [NextAttemptAt], [OccurredAt]);
GO

CREATE INDEX [IX_OutboxMessages_TenantId] ON [outbox].[OutboxMessages] ([TenantId]);
GO
