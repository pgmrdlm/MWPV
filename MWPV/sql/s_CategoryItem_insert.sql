-- s_CategoryItem_insert.sql
INSERT INTO CategoryItem (
    Category_Key,
    CI_Name,
    CI_Description,
    CI_Username,
    CI_SignInUrl,
    CI_AccountEmail,
    CI_AccountPhoneNumber,
    CI_SecretMeta,
    CI_SecretData,
    CI_SecretStorage,
    IsActive
)
VALUES (
    @Category_Key,
    @CI_Name,
    @CI_Description,
    @CI_Username,
    @CI_SignInUrl,
    @CI_AccountEmail,
    @CI_AccountPhoneNumber,
    @CI_SecretMeta,
    @CI_SecretData,
    @CI_SecretStorage,
    COALESCE(@IsActive, 1)
);

SELECT last_insert_rowid() AS ItemId;
