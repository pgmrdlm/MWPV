-- s_CategoryItemPasswordHistory_check_reuse_365days.sql
SELECT
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM CategoryItemPasswordHistory h
            WHERE h.CIPaH_ItemId = @CIPaH_ItemId
              AND h.CIPaH_PwSig  = @CIPaH_PwSig
              AND h.CIPaH_CreatedAt >= (julianday('now') - 365.0)
        )
        THEN 1 ELSE 0
    END AS IsReuseWithin365Days;
