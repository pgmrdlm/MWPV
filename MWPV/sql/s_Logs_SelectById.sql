-- Logs_Select_ById.sql
SELECT
    Id,
    CreatedUtc,
    Level,
    Source,
    EventCode
FROM Logs
WHERE Id = @id
LIMIT 1;