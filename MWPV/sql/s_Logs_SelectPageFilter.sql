-- Logs_Select_PageFilter.sql
-- Params: @limit INT, @offset INT, @filter_code TEXT (nullable)

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
WHERE (@filter_code IS NULL OR EventCode = @filter_code)
ORDER BY CreatedUtc DESC
LIMIT @limit OFFSET @offset;
