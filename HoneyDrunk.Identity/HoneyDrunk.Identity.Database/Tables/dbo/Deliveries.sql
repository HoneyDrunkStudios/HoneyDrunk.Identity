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
