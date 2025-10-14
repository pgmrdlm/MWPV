-- params: @Sig TEXT
-- returns one row if a log with this content-signature already exists
SELECT 1
FROM Logs
WHERE StackHash = @Sig
LIMIT 1;
