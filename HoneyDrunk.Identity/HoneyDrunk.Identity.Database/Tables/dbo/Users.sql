CREATE TABLE [dbo].[Users] (
    [UserId] varchar(30) NOT NULL,
    [State] nvarchar(20) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [RequestedAt] datetimeoffset NULL,
    [DeletionPausedAt] datetimeoffset NULL,
    [RecoveryDeadline] datetimeoffset NULL,
    [Version] bigint NOT NULL DEFAULT 0,
    [ChangedAt] datetimeoffset NULL,
    [SessionsRevoked] bit NOT NULL DEFAULT 0,
    [NextAttemptAt] datetimeoffset NULL,
    [FailureCode] nvarchar(max) NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([UserId])
);
GO
