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
    AS_PW_IncludeSymbols,
    AS_LogRetentionDays,
    AS_BackupRetentionCount
)
SELECT
    12,
    10,
    10,
    1,
    45,
    1,
    30,
    5
WHERE NOT EXISTS (SELECT 1 FROM AppSettings);

UPDATE AppSettings
SET
    AS_PW_Minimum = @AS_PW_Minimum,
    AS_PW_IncludeSymbols = @AS_PW_IncludeSymbols,
    SensitiveClipboardClearSeconds = @SensitiveClipboardClearSeconds,
    AS_LogRetentionDays = @AS_LogRetentionDays,
    AS_BackupRetentionCount = @AS_BackupRetentionCount
WHERE rowid = (
    SELECT rowid
    FROM AppSettings
    ORDER BY rowid
    LIMIT 1
);
