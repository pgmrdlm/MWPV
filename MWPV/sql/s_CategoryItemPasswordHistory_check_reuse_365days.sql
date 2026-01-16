-- File: sql/s_CategoryItemPasswordHistory_check_reuse_365days.sql
--
-- PURPOSE
-- - GLOBAL duplicate password fingerprint check across the entire vault.
-- - Kept under the same filename for now to avoid churn.
--
-- INPUTS (kept the same to avoid breaking call sites)
-- - @CIPaH_ItemId (INTEGER) : accepted but NOT used in the global check
-- - @CIPaH_PwFp   (BLOB)    : stable keyed fingerprint
--
-- OUTPUT
-- - IsReuseWithin365Days (0/1) : name kept for now

SELECT
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM CategoryItemPasswordHistory h
            WHERE h.CIPaH_PwFp = @CIPaH_PwFp
            LIMIT 1
        )
        THEN 1 ELSE 0
    END AS IsReuseWithin365Days;
