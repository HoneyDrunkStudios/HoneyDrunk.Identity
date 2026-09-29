CREATE TABLE [dbo].[Subjects] (
    [SubjectKey] varchar(64) NOT NULL,
    [Issuer] nvarchar(max) NOT NULL DEFAULT N'',
    [Subject] nvarchar(max) NOT NULL DEFAULT N'',
    [ObjectId] nvarchar(max) NULL,
    [UserId] varchar(30) NOT NULL,
    CONSTRAINT [PK_Subjects] PRIMARY KEY ([SubjectKey]),
    CONSTRAINT [FK_Subjects_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([UserId]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_Subjects_UserId] ON [dbo].[Subjects] ([UserId]);
GO
