-- s_CategoryItem_update.sql
UPDATE CategoryItem
SET
    CI_Name               = @CI_Name,
    CI_Description        = @CI_Description,
    CI_Username           = @CI_Username,
    CI_SignInUrl          = @CI_SignInUrl,
    CI_BookMarkOnly       = COALESCE(@CI_BookMarkOnly, CI_BookMarkOnly),
    CI_AccountEmail       = @CI_AccountEmail,
    CI_AccountPhoneNumber = @CI_AccountPhoneNumber,
    CI_SecretMeta         = @CI_SecretMeta,
    CI_SecretData         = @CI_SecretData,
    CI_SecretStorage      = @CI_SecretStorage,
    IsActive              = COALESCE(@IsActive, IsActive),
    CI_UpdateUTC          = strftime('%s','now')
WHERE ItemId = @ItemId;

SELECT changes() AS RowsAffected;
