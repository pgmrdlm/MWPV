/* File: sql/s_CategoryItemSecurityQuestions_select_by_itemid_and_id.sql */

SELECT
    CISQ_Id        AS Id,
    CISQ_ItemId    AS ItemId,
    CISQ_Seq       AS Seq,
    CISQ_Question  AS Question,
    CISQ_Answer    AS Answer,
    CISQ_IsActive  AS IsActive,
    CISQ_CreatedAt AS CreatedAtUtcSeconds,
    CISQ_UpdatedAt AS UpdatedAtUtcSeconds
FROM CategoryItemSecurityQuestions
WHERE CISQ_ItemId = @ItemId
  AND CISQ_Id = @Id
LIMIT 1;
