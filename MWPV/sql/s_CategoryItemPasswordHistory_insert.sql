-- s_CategoryItemPasswordHistory_insert.sql
INSERT INTO CategoryItemPasswordHistory (
    CIPaH_ItemId,
    CIPaH_Password,
    CIPaH_PadLen,
    CIPaH_PwSig
)
VALUES (
    @CIPaH_ItemId,
    @CIPaH_Password,
    @CIPaH_PadLen,
    @CIPaH_PwSig
)
RETURNING CIPaH_PwHistId;
