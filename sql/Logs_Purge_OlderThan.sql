-- Deletes logs older than the cutoff (UTC)
DELETE FROM Logs
WHERE WhenUtc < @CutoffUtc;
