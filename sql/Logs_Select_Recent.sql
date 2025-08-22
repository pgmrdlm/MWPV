-- Logs_Select_Recent.sql
-- params: @CrashesOnly INTEGER (NULL|0|1), @FromUtc TEXT or NULL (ISO8601), @Limit INTEGER
SELECT
    Id,
    WhenUtc,
    CreatedUtc,              -- REQUIRED for the debug reader
    Level,
    Source,
    EventCode,
    SessionId,
    AppVersion,
    IsCrash,
    Payload,
    PayloadFmt,
    StackHash
FROM Logs
WHERE (@CrashesOnly IS NULL OR @CrashesOnly = 0 OR Level IN ('ERROR','FATAL'))
  AND (@FromUtc IS NULL OR CreatedUtc >= @FromUtc)
ORDER BY CreatedUtc DESC
LIMIT @Limit;
