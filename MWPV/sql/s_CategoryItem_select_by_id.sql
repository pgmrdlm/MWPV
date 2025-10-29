SELECT
  ItemId,
  Category_Key,
  CI_Name,
  CI_Description,
  CI_SecretData,
  CI_SecretMeta
FROM CategoryItem
WHERE ItemId = @ItemId;
