DELETE FROM Logs
WHERE EventCode IN ('SESSION_START', 'SESSION_END')
  AND julianday(WhenUtc) < julianday(@CutoffUtc);
