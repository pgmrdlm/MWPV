-- Logs_Select_Page.sql
-- Params: @limit INT, @offset INT
-- Returns a page of recent logs without filters, including extended metadata.

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
