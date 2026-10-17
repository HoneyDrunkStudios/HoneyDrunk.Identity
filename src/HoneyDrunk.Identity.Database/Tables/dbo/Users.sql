CREATE TABLE [dbo].[Users] (
    [UserId] varchar(30) NOT NULL,
    [State] nvarchar(20) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [RequestedAt] datetimeoffset NULL,
    [DeletionPausedAt] datetimeoffset NULL,
    [RecoveryDeadline] datetimeoffset NULL,
    [Version] bigint NOT NULL CONSTRAINT [DF_Users_Version] DEFAULT 0,
    [ChangedAt] datetimeoffset NULL,
    [SessionsRevoked] bit NOT NULL CONSTRAINT [DF_Users_SessionsRevoked] DEFAULT 0,
    [NextAttemptAt] datetimeoffset NULL,
    [FailureCode] nvarchar(max) NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([UserId])
);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'One stable Identity account and its current deletion lifecycle. Restricted personal data. Mutable, with CreatedAt and lifecycle ChangedAt; transitions are audited through HoneyDrunk.Audit, without temporal history. One row per account; verified erasure deletes the row.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Stable application-generated account identifier: usr_ followed by a ULID. Used across product nodes; never a provider subject.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'UserId';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Current lifecycle state written by Identity: Active, Inactive or Erasing. Only Active permits ordinary account access.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'State';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC instant the account was first resolved from a verified external subject.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'CreatedAt';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC instant of the current confirmed deletion request. Null means no current deletion request, including after cancellation.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'RequestedAt';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC instant downstream commitments were first paused for the current lifecycle. Retained after cancellation; null means no deletion pause has been recorded.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'DeletionPausedAt';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC end of the original 30-day recovery window. Null means no current deletion request; retries do not extend the deadline.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'RecoveryDeadline';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Monotonically increasing lifecycle transition version, initially zero. Consumers reject acknowledgments for other versions; this is not a SQL rowversion.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'Version';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC instant of the latest lifecycle state transition. Null means the account has not yet transitioned from its initial state.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'ChangedAt';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'True after all linked provider sessions were successfully revoked for the current deletion request; reset to false for a new request.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'SessionsRevoked';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC earliest retry instant for lifecycle maintenance. Null allows an immediate attempt.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'NextAttemptAt';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Safe operational code such as ProviderLifecycleUnavailable or AwaitingConsumerAcknowledgment; never exception text. Null means no outstanding recorded lifecycle failure.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Users', @level2type = N'COLUMN', @level2name = N'FailureCode';
GO
