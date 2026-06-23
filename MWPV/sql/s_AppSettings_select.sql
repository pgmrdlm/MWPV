-- File: sql/s_AppSettings_select.sql
--
-- Purpose:
-- - Load application password settings for runtime UI policy.
--
SELECT
    AS_PW_Minimum,
    AS_PW_Incriments,
    AS_PW_Inctriment_Steps
FROM AppSettings
LIMIT 1;
