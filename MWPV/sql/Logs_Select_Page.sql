SELECT
  Id,
  CreatedUtc,
  Level,
  Source,
  EventCode,
  MachineId,
  AppVersion,
  IsCrash,
  LoginId,
  ItemId,
  DeviceMake,
  DeviceModel,
  OSVersion,
  InstallType
FROM Logs
ORDER BY CreatedUtc DESC
LIMIT @limit OFFSET @offset;
