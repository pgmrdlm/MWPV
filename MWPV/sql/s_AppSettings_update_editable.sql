-- File: sql/s_AppSettings_update_editable.sql
--
-- Purpose:
-- - Save controlled user-editable AppSettings values.
--
INSERT INTO AppSettings (
    AS_PW_Minimum,
    AS_PW_Incriments,
    AS_PW_Inctriment_Steps,
    AS_DisplayCategoriesWithItems,
    SensitiveClipboardClearSeconds,
    AS_InactivityTimeoutMinutes,
    AS_LogRetentionDays,
    AS_BackupRetentionCount,
    AS_BackupPromptOnExitAfterChanges
)
SELECT
    12,
    10,
    10,
    1,
    45,
    4,
    30,
    5,
    1
WHERE NOT EXISTS (SELECT 1 FROM AppSettings);

UPDATE AppSettings
SET
    AS_PW_Minimum = @AS_PW_Minimum,
    AS_InactivityTimeoutMinutes = @AS_InactivityTimeoutMinutes,
    SensitiveClipboardClearSeconds = @SensitiveClipboardClearSeconds,
    AS_LogRetentionDays = @AS_LogRetentionDays,
    AS_BackupRetentionCount = @AS_BackupRetentionCount,
    AS_BackupPromptOnExitAfterChanges = @AS_BackupPromptOnExitAfterChanges
WHERE rowid = (
    SELECT rowid
    FROM AppSettings
    ORDER BY rowid
    LIMIT 1
);
