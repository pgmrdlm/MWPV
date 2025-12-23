-- s_CategoryItemPasswordHistory_select_most_recent.sql
SELECT
    CIPaH_PwHistId,
    CIPaH_ItemId,
    CIPaH_CreatedAt,
    CIPaH_Version,
    CIPaH_Password,
    CIPaH_PadLen,
    CIPaH_PwSig,
    CIPaH_SigVersion
FROM CategoryItemPasswordHistory
WHERE CIPaH_ItemId = @CIPaH_ItemId
ORDER BY CIPaH_CreatedAt DESC, CIPaH_PwHistId DESC
LIMIT 1;
