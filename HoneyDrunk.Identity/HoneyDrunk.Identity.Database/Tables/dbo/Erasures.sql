CREATE TABLE [dbo].[Erasures] (
    [UserId] varchar(30) NOT NULL,
    [ErasedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_Erasures] PRIMARY KEY ([UserId])
);
GO
