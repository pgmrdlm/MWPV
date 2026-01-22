-- Logs_Select_ById.sql
SELECT
    Id,
    CreatedUtc,
    Level,
    Source,
    EventCode,
    SubjectText,
    MessageText
FROM Logs
WHERE Id = @id
LIMIT 1;
