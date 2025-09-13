-- Logs_Select_Page_Filter.sql
-- Params: @limit INT, @offset INT, @filter_code TEXT (nullable)
SELECT
    Id,
    CreatedUtc,
    Level,
    Source,
    EventCode
FROM Logs
WHERE (@filter_code IS NULL OR EventCode = @filter_code)
ORDER BY CreatedUtc DESC
LIMIT @limit OFFSET @offset;
