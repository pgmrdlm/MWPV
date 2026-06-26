/* File: sql/s_CategoryItemSecurityQuestions_select_all_by_itemid.sql */

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
ORDER BY CISQ_Seq ASC, CISQ_Id ASC;
