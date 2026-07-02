-- s_CategoryItem_select_by_id.sql
SELECT
    ci.ItemId,
    ci.Category_Key,
    ci.CI_Name,
    ci.CI_Description,
    ci.CI_Username,
    ci.CI_SignInUrl,
    ci.CI_BookMarkOnly,
    ci.CI_AccountEmail,
    ci.CI_AccountPhoneNumber,
    ci.CI_Pin,
    ci.CI_CreateUTC,
    ci.CI_UpdateUTC,
    ci.IsActive,
    IFNULL(c.IsActive, 1) AS CategoryIsActive
FROM CategoryItem ci
LEFT JOIN Category c
  ON c.Category_Key = ci.Category_Key
WHERE ci.ItemId = @ItemId;
