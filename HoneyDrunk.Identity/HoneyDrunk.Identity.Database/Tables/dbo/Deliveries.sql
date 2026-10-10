CREATE TABLE [dbo].[Deliveries] (
    [UserId] varchar(30) NOT NULL,
    [Consumer] nvarchar(100) NOT NULL,
    [Destination] nvarchar(max) NOT NULL,
    [Version] bigint NOT NULL,
    [Acknowledgment] nvarchar(max) NOT NULL,
    [Acknowledged] bit NOT NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_Deliveries] PRIMARY KEY ([UserId], [Consumer])
);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'One current lifecycle delivery acknowledgment per Identity account and registered consumer. Restricted data, including a bearer acknowledgment capability. Mutable retry plumbing without row history; lifecycle transitions use HoneyDrunk.Audit. Rows are reused across transitions and deleted on verified account erasure.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Deliveries';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Identity account for this delivery, logically referencing dbo.Users.UserId. Current schema does not enforce this relationship; erasure removes delivery rows before the account.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Deliveries', @level2type = N'COLUMN', @level2name = N'UserId';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Trusted consumer node identifier from the environment-owned lifecycle registry; forms the composite key with UserId.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Deliveries', @level2type = N'COLUMN', @level2name = N'Consumer';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Private queue destination copied from the trusted consumer registry; never supplied by a public request.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Deliveries', @level2type = N'COLUMN', @level2name = N'Destination';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Account lifecycle transition version for which this acknowledgment is valid.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Deliveries', @level2type = N'COLUMN', @level2name = N'Version';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Cryptographically random 32-byte capability encoded as 64 hexadecimal characters. Do not log or expose outside private lifecycle transport.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Deliveries', @level2type = N'COLUMN', @level2name = N'Acknowledgment';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'True only after the matching consumer confirms the current unexpired capability following its committed mutation.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Deliveries', @level2type = N'COLUMN', @level2name = N'Acknowledged';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC expiry of the current acknowledgment capability, initially one hour after it is queued. Retries replace expired capabilities.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Deliveries', @level2type = N'COLUMN', @level2name = N'ExpiresAt';
GO
