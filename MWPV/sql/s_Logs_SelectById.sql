-- Logs_Select_ById.sql
SELECT
    Id,
    CreatedUtc,
    Level,
    Source,
    EventCode,
    PayloadFmt,
    CASE WHEN Payload IS NULL THEN 0 ELSE length(Payload) END AS PayloadSize,
    Payload
FROM Logs
WHERE Id = @id
LIMIT 1;