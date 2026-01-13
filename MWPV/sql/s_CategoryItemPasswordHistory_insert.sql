-- File: sql/s_CategoryItemPasswordHistory_insert.sql

INSERT INTO CategoryItemPasswordHistory
(
    CIPaH_ItemId,
    CIPaH_Version,
    CIPaH_Password,
    CIPaH_PadLen,
    CIPaH_PwSig,
    CIPaH_SigVersion
)
VALUES
(
    @ItemId,
    COALESCE(@Version, 1),
    @PasswordBlob,
    @PadLen,
    @PwSig,
    COALESCE(@SigVersion, 1)
);

-- Return the new history PK (same pattern we use elsewhere)
SELECT last_insert_rowid() AS PwHistId;
