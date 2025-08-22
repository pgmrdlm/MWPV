-- Logs_Select_Recent.sql
-- params:
--   @CrashesOnly  INTEGER  (NULL|0|1)   -- 1 => only ERROR/FATAL, else all
--   @FromUtc      TEXT     (ISO8601) or NULL
--   @Limit        INTEGER  (required; if NULL/<=0 defaults to 50)

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
WHERE
    -- If @CrashesOnly = 1, restrict to ERROR/FATAL; otherwise allow all
    ( @CrashesOnly IS NULL
      OR @CrashesOnly = 0
      OR Level IN ('ERROR','FATAL') )
  AND
    -- If @FromUtc is provided, filter by CreatedUtc >= @FromUtc
    ( @FromUtc IS NULL
      OR CreatedUtc >= @FromUtc )
ORDER BY CreatedUtc DESC
LIMIT CASE
          WHEN @Limit IS NULL OR @Limit <= 0 THEN 50
          ELSE @Limit
      END;
