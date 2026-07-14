SELECT
    COUNT(*) AS TotalCount,
    COALESCE(SUM(CASE WHEN EventCode = 'SESSION_START' THEN 1 ELSE 0 END), 0) AS SessionStartCount,
    COALESCE(SUM(CASE WHEN EventCode = 'SESSION_END' THEN 1 ELSE 0 END), 0) AS SessionEndCount,
    MIN(WhenUtc) AS OldestWhenUtc,
    MAX(WhenUtc) AS NewestWhenUtc
FROM Logs
WHERE EventCode IN ('SESSION_START', 'SESSION_END')
  AND julianday(WhenUtc) < julianday(@CutoffUtc);
