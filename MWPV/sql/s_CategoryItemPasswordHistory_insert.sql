-- File: sql/s_CategoryItemPasswordHistory_insert.sql
--
-- PwFp version (stable fingerprint)
-- Matches DDL:
--   CIPaH_PwFp      BLOB    NOT NULL
--   CIPaH_FpVersion INTEGER NOT NULL DEFAULT 1
--
-- Matches service params:
--   @ItemId, @Version, @PasswordBlob, @PadLen, @PwFp, @FpVersion

INSERT INTO CategoryItemPasswordHistory
(
    CIPaH_ItemId,
    CIPaH_Version,
    CIPaH_Password,
    CIPaH_PadLen,
    CIPaH_PwFp,
    CIPaH_FpVersion
)
VALUES
(
    @ItemId,
    COALESCE(@Version, 1),
    @PasswordBlob,
    @PadLen,
    @PwFp,
    COALESCE(@FpVersion, 1)
);

-- Return the new history PK (same pattern we use elsewhere)
SELECT last_insert_rowid() AS PwHistId;
