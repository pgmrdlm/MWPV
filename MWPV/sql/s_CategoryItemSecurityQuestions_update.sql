/* File: sql/s_CategoryItemSecurityQuestions_update.sql */

UPDATE CategoryItemSecurityQuestions
SET
    CISQ_Seq       = @Seq,
    CISQ_Question  = @Question,
    CISQ_Answer    = @Answer,
    CISQ_IsActive  = @IsActive,
    CISQ_UpdatedAt = CAST(strftime('%s','now') AS INTEGER)
WHERE CISQ_Id = @Id
  AND CISQ_ItemId = @ItemId;
