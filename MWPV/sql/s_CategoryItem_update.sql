UPDATE CategoryItem
SET
  CI_Name        = @Name,
  CI_Description = @Description,
  CI_SecretData  = @SecretData,
  CI_SecretMeta  = @SecretMeta,
  CI_UpdateUTC   = strftime('%s','now')
WHERE ItemId = @ItemId;
