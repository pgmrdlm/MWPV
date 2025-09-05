-- Logs_Select_Page.sql
SELECT
  Id,
  WhenUtc,
  CreatedUtc,
  Level,
  Source,
  EventCode,
  SessionId,
  MachineId,
  AppVersion,
  IsCrash,
  PayloadFmt,
  PayloadVer,
  KeySetVersion,
  StackHash,
  length(Payload) as PayloadSize
FROM Logs
ORDER BY Id DESC
LIMIT $take OFFSET $skip;
