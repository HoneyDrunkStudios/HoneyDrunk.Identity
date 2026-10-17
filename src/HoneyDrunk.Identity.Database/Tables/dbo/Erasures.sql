CREATE TABLE [dbo].[Erasures] (
    [UserId] varchar(30) NOT NULL,
    [ErasedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_Erasures] PRIMARY KEY ([UserId])
);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'One minimal verified-erasure marker for an erased Identity account. Restricted pseudonymous data. Restore-barrier plumbing, without temporal history; ErasedAt records the completed erasure. The marker intentionally outlives the deleted account, has no account FK, and is pruned after 35 days.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Erasures';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'Former dbo.Users.UserId identifying the erased account for restore barriers. The referenced account is deleted, so this must not be an enforced FK.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Erasures', @level2type = N'COLUMN', @level2name = N'UserId';
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'UTC instant provider erasure and local purge completed, or the authoritative instant reapplied from the recovery ledger. Starts the 35-day retention window.',
    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Erasures', @level2type = N'COLUMN', @level2name = N'ErasedAt';
GO
