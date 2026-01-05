-- s_CategoryItem_insert.sql
INSERT INTO CategoryItem (
    Category_Key,
    CI_Name,
    CI_Description,
    CI_Username,
    CI_SignInUrl,
    CI_BookMarkOnly,
    CI_AccountEmail,
    CI_AccountPhoneNumber,
    CI_Pin,
    IsActive
)
VALUES (
    @Category_Key,
    @CI_Name,
    @CI_Description,
    @CI_Username,
    @CI_SignInUrl,
    COALESCE(@CI_BookMarkOnly, 0),
    @CI_AccountEmail,
    @CI_AccountPhoneNumber,
    @CI_Pin,
    COALESCE(@IsActive, 1)
)
RETURNING ItemId;
