INSERT INTO CategoryItem
(
  Category_Key, CI_Name, CI_Description,
  CI_SecretData, CI_SecretMeta
)
VALUES
(
  @CategoryKey, @Name, @Description,
  @SecretData, @SecretMeta
);
SELECT last_insert_rowid() AS NewId;
