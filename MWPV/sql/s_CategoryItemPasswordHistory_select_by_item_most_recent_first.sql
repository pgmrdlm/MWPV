-- File: sql/s_CategoryItemPasswordHistory_select_by_item_most_recent_first.sql
--
-- PwFp version (stable fingerprint)
SELECT
    CIPaH_PwHistId,
    CIPaH_ItemId,
    CIPaH_CreatedAt,
    CIPaH_Version,
    CIPaH_Password,
    CIPaH_PadLen,
    CIPaH_PwFp,
    CIPaH_FpVersion
FROM CategoryItemPasswordHistory
WHERE CIPaH_ItemId = @CIPaH_ItemId
ORDER BY CIPaH_CreatedAt DESC, CIPaH_PwHistId DESC;
