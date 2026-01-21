-- File: sql/s_LogMessageTemplate_SelectAll.sql
--
-- Purpose:
-- - Load all ACTIVE log message templates into non-sensitive memory (cache/dictionary).
-- - Generic rows only (no secrets).
--
SELECT
    UpdateForm,
    Seq,
    LogMessage,
    Active
FROM LogMessageTemplate
WHERE Active = 1
ORDER BY UpdateForm, Seq;
