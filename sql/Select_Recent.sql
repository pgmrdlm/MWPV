SELECT Id, Payload
FROM Logs
WHERE (@CrashesOnly = 0 OR IsCrash = 1)
ORDER BY Id DESC
LIMIT @Limit;
