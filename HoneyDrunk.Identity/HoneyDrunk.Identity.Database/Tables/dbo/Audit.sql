CREATE TABLE [dbo].[Audit] (
    [Id] bigint NOT NULL IDENTITY,
    [OccurredAt] datetimeoffset NOT NULL,
    [EventName] nvarchar(100) NOT NULL,
    [Outcome] nvarchar(40) NOT NULL,
    [ActorHash] varchar(64) NOT NULL,
    CONSTRAINT [PK_Audit] PRIMARY KEY ([Id])
);
GO
