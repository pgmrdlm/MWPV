/* File: sql/s_CategoryItemSecurityQuestions_insert.sql */

INSERT INTO CategoryItemSecurityQuestions
(
    CISQ_ItemId,
    CISQ_Seq,
    CISQ_Question,
    CISQ_Answer,
    CISQ_IsActive
)
VALUES
(
    @ItemId,
    @Seq,
    @Question,
    @Answer,
    @IsActive
);

SELECT last_insert_rowid() AS Id;
