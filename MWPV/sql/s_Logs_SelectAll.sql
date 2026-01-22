-- Logs_SelectAll.sql

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
    SubjectText,
    MessageText,
    DeviceMake,
    DeviceModel,
    OSVersion,
    InstallType
FROM Logs
ORDER BY CreatedUtc DESC;
