-- s_CategoryItem_select_by_id.sql
SELECT
    ItemId,
    Category_Key,
    CI_Name,
    CI_Description,
    CI_Username,
    CI_SignInUrl,
    CI_BookMarkOnly,
    CI_AccountEmail,
    CI_AccountPhoneNumber,
    CI_SecretMeta,
    CI_SecretData,
    CI_SecretStorage,
    CI_CreateUTC,
    CI_UpdateUTC,
    IsActive
FROM CategoryItem
WHERE ItemId = @ItemId;
