/* File: sql/s_CategoryItemSecurityQuestions_deactivate.sql */

UPDATE CategoryItemSecurityQuestions
SET
    CISQ_IsActive  = 0,
    CISQ_UpdatedAt = CAST(strftime('%s','now') AS INTEGER)
WHERE CISQ_Id = @Id
  AND CISQ_ItemId = @ItemId;
