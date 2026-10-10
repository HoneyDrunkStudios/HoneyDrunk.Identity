CREATE TABLE [dbo].[Subjects] (
    [SubjectKey] varchar(64) NOT NULL,
    [Issuer] nvarchar(max) NOT NULL CONSTRAINT [DF_Subjects_Issuer] DEFAULT N'',
    [Subject] nvarchar(max) NOT NULL CONSTRAINT [DF_Subjects_Subject] DEFAULT N'',
    [ObjectId] nvarchar(max) NULL,
    [UserId] varchar(30) NOT NULL,
    CONSTRAINT [PK_Subjects] PRIMARY KEY ([SubjectKey]),
    CONSTRAINT [FK_Subjects_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([UserId]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_Subjects_UserId] ON [dbo].[Subjects] ([UserId]);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'One verified issuer-subject binding to an Identity account. Restricted personal data. Current mapping only, without temporal history; account/link actions use HoneyDrunk.Audit. Growth follows linked external identities; verified account erasure removes its bindings.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Subjects';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Uppercase hexadecimal SHA-256 of the canonical JSON issuer-subject pair; uniquely identifies the verified external binding.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Subjects', @level2type = N'COLUMN', @level2name = N'SubjectKey';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Issuer URI from the validated external identity. Empty string is the legacy default, not a trusted issuer.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Subjects', @level2type = N'COLUMN', @level2name = N'Issuer';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Subject claim from the validated external identity, scoped by Issuer. Empty string is the legacy default, not a verified subject.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Subjects', @level2type = N'COLUMN', @level2name = N'Subject';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Provider directory object identifier from validated identity claims, used for Graph lifecycle operations. Null means no provider object identifier has been captured.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Subjects', @level2type = N'COLUMN', @level2name = N'ObjectId';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Identity account owning this binding, referencing dbo.Users.UserId. The binding must be removed before account deletion.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Subjects', @level2type = N'COLUMN', @level2name = N'UserId';
GO
