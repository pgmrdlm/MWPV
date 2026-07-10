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
WHERE
    @filter_code IS NULL
    OR EventCode = @filter_code
    OR (
        @filter_code = 'APP_SETTINGS'
        AND EventCode IN (
            'APP_SETTING_UPDATED',
            'APP_SETTING_RESET',
            'APP_SETTING_RESET_ALL'
        )
    )
ORDER BY CreatedUtc DESC
LIMIT @limit OFFSET @offset;
