/* ============================================================================
   MWPV - 01.21 -> 01.22 UPGRADE
   Backup-on-exit success logging foundation.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT 'BackupOnExit', 1, 'A verified backup containing #FileCount# file(s) was created while closing MWPV after vault changes.', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate
    WHERE UpdateForm = 'BackupOnExit'
      AND Seq = 1
);

UPDATE LogMessageTemplate
SET LogMessage = 'A verified backup containing #FileCount# file(s) was created while closing MWPV after vault changes.',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'BackupOnExit'
  AND Seq = 1;

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    22,
    'BACKUP_CREATED_ON_EXIT',
    'Backup created on exit',
    1
FROM ComboType ct
WHERE ct.Code = 'log_filters'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail d
      WHERE d.ComboTypeId = ct.ComboTypeId
        AND d.Code = 'BACKUP_CREATED_ON_EXIT'
  );

UPDATE ComboDetail
SET Seq = 22,
    Description = 'Backup created on exit',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'log_filters'
        LIMIT 1
    )
  AND Code = 'BACKUP_CREATED_ON_EXIT';

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.22',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.21 to 01.22',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.22'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.22' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
