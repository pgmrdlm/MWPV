/* File: sql/s_CategoryItemAccounts_insert.sql */

INSERT INTO CategoryItemAccounts
(
    ItemId,
    Label,
    Number,
    AccountTypeId,
    AccountTypeFreeform,
    IsActive
)
VALUES
(
    @ItemId,
    @Label,
    @Number,
    @AccountTypeId,
    @AccountTypeFreeform,
    @IsActive
);

SELECT last_insert_rowid() AS Id;
