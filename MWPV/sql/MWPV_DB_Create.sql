/* ============================================================================
   MWPV - MASTER DDL (FULL REWRITE WITH SEEDS)  -- v2025-10-16b
   Fix: seed inserts now use UNION ALL SELECT blocks (no VALUES(...) alias).
   Change in this revision: add back ComboType 'log_filters' + ComboDetail rows.
   This edition: REMOVED CREATE TABLE for CategoryItemSecurityQuestions, BankCards, CategoryItemAccounts.********** after we do the ddl and scripts., we need a view on the phone number.  ***** script names should be built with following naming standards_s_<tablename>_<description>.sql
   2025-11-24 edit: Category_Type no longer FK to ComboDetail; defaults to 0.
                    Removed category_types ComboType/ComboDetail seeding and
                    rewrote starter categories to use Category_Type = 0.

   2025-12-23 edit (per current discussion):
   - CategoryItem: removed CI_MFAType, CI_MFABackupCodes, CI_NbrSecurityQuestions
   - CategoryItemPasswordHistory: CIPaH_CreatedAt now defaults to now (epoch seconds)
   - Added supporting index for "most recent first" access
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- Drops
DROP VIEW  IF EXISTS vw_CurrentPassword;
DROP VIEW  IF EXISTS vw_CurrentPin;

DROP TABLE IF EXISTS ComboDetail;
DROP TABLE IF EXISTS ComboType;
DROP TABLE IF EXISTS CategoryItemPasswordHistory;
DROP TABLE IF EXISTS CategoryItemSecurityQuestions;
DROP TABLE IF EXISTS CategoryItemAccounts;
DROP TABLE IF EXISTS BankCards;
DROP TABLE IF EXISTS CategoryItem;
DROP TABLE IF EXISTS Category;
DROP TABLE IF EXISTS KeyArchiveIntegrity;
DROP TABLE IF EXISTS DbVersion;
DROP TABLE IF EXISTS Logs;
/* =========================
   NEW TABLE: LogMessageTemplate
   (Add this ONE drop line into your Drops section)
   ========================= */
DROP TABLE IF EXISTS LogMessageTemplate;

DROP TABLE IF EXISTS CategoryItemPinHistory;

COMMIT;
PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

-- Lookups
CREATE TABLE ComboType (
    ComboTypeId   INTEGER  PRIMARY KEY AUTOINCREMENT,
    Code          TEXT     NOT NULL UNIQUE,
    Description   TEXT     NOT NULL,
    Active        INTEGER  NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc    TEXT     NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc    TEXT     NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE ComboDetail (
    ComboDetailId   INTEGER  PRIMARY KEY AUTOINCREMENT,
    ComboTypeId     INTEGER  NOT NULL REFERENCES ComboType (ComboTypeId) ON DELETE CASCADE,
    Seq             INTEGER  NOT NULL,
    Code            TEXT     NOT NULL,
    Description     TEXT     NOT NULL,
    Active          INTEGER  NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc      TEXT     NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc      TEXT     NOT NULL DEFAULT (datetime('now'))
);

-- Categories
CREATE TABLE Category (
    Category_Key         INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Name        TEXT    NOT NULL UNIQUE,
    Category_Description TEXT,
    Category_Type        INTEGER NOT NULL DEFAULT 0,
    CreatedUtc           TEXT    NOT NULL DEFAULT (STRFTIME('%Y-%m-%dT%H:%M:%fZ','now')),
    IsActive             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

-- CategoryItem (Basic tab only; masked fields stored encrypted as BLOBs)
CREATE TABLE CategoryItem (
    ItemId                   INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Key             INTEGER NOT NULL REFERENCES Category (Category_Key) ON DELETE CASCADE,

    CI_Name                  TEXT    NOT NULL,
    CI_Description           TEXT,
    CI_Username              TEXT,
    CI_SignInUrl             TEXT,

    CI_BookMarkOnly          INTEGER NOT NULL DEFAULT 0 CHECK (CI_BookMarkOnly IN (0,1)),

    -- Masked/sensitive (store ciphertext bytes)
    CI_AccountEmail          BLOB,
    CI_AccountPhoneNumber    BLOB,
    CI_Pin                   BLOB,

    CI_CreateUTC             INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CI_UpdateUTC             INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    IsActive                 INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),

    CHECK (length(trim(CI_Name)) > 0),
    UNIQUE (Category_Key, CI_Name COLLATE NOCASE)
);


-- Encrypted detail siblings
CREATE TABLE CategoryItemPasswordHistory (
    CIPaH_PwHistId    INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPaH_ItemId      INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPaH_CreatedAt   REAL NOT NULL DEFAULT (julianday('now')),
    CIPaH_Version     INTEGER NOT NULL DEFAULT 1,
    CIPaH_Password    BLOB    NOT NULL,
    CIPaH_PadLen      INTEGER,
    CIPaH_PwSig       BLOB    NOT NULL,
    CIPaH_SigVersion  INTEGER NOT NULL DEFAULT 1
);

-- Support: fast “most recent first” per item (ORDER BY CreatedAt DESC, PwHistId DESC)
CREATE INDEX IF NOT EXISTS IX_CategoryItemPasswordHistory_Item_CreatedAt
ON CategoryItemPasswordHistory (CIPaH_ItemId, CIPaH_CreatedAt, CIPaH_PwHistId);

/* =========================
   CategoryItemSecurityQuestions
   ========================= */
CREATE TABLE CategoryItemSecurityQuestions (
    CISQ_Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    CISQ_ItemId    INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CISQ_Seq       INTEGER NOT NULL,           -- display/order index
    CISQ_Question  BLOB    NOT NULL,           -- encrypted question text
    CISQ_Answer    BLOB    NOT NULL,           -- encrypted answer
    CISQ_CreatedAt INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CISQ_UpdatedAt INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    UNIQUE (CISQ_ItemId, CISQ_Seq)
);

/* =========================
   BankCards
   (Card type points to ComboDetail; seed the card-type ComboDetail separately)
   ========================= */
CREATE TABLE BankCards (
    BC_Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    BC_ItemId        INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    BC_CardType      INTEGER NOT NULL REFERENCES ComboDetail (ComboDetailId), -- e.g., CREDIT/DEBIT/STORE/etc.
    BC_Cardholder    TEXT,                  -- optional display name (non-secret)
    BC_Number        BLOB    NOT NULL,      -- encrypted PAN (digits only; Luhn-validated in app)
    BC_ExpMonth      INTEGER NOT NULL CHECK (BC_ExpMonth BETWEEN 1 AND 12),
    BC_ExpYear       INTEGER NOT NULL,      -- 4-digit year (range validated in app)
    BC_CVV           BLOB,                  -- encrypted
    BC_Pin           BLOB,                  -- encrypted (if applicable)
    BC_BillingZip    BLOB,                  -- encrypted (if applicable)
    BC_IsPrimary     INTEGER NOT NULL DEFAULT 0 CHECK (BC_IsPrimary IN (0,1)),
    BC_IsActive      INTEGER NOT NULL DEFAULT 1 CHECK (BC_IsActive IN (0,1)),
    BC_CreatedAt     INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    BC_UpdatedAt     INTEGER NOT NULL DEFAULT (strftime('%s','now'))
);


/* =========================
   CategoryItemAccounts
   (General account numbers separate from cards)
   ========================= */
CREATE TABLE CategoryItemAccounts (
    CIA_Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    CIA_ItemId        INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIA_AccountLabel  TEXT,                 -- friendly label (non-secret)
    CIA_AccountNumber BLOB    NOT NULL,     -- encrypted (normalize/validate in app)
    CIA_RoutingNumber BLOB,                 -- encrypted (US ABA) - optional
    CIA_IBAN          BLOB,                 -- encrypted - optional
    CIA_SWIFT         BLOB,                 -- encrypted - optional
    CIA_Meta          BLOB,                 -- encrypted misc (e.g., branch, notes)

    -- NEW: optional type from combo, same principle as BankCards
    CIA_AccountType      INTEGER REFERENCES ComboDetail (ComboDetailId),  -- from ComboType='account_types'
    CIA_AccountTypeOther TEXT,                 -- user freeform when type = OTHER

    CIA_IsPrimary     INTEGER NOT NULL DEFAULT 0 CHECK (CIA_IsPrimary IN (0,1)),
    CIA_CreatedAt     INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CIA_UpdatedAt     INTEGER NOT NULL DEFAULT (strftime('%s','now'))
);

-- Integrity / versions / logs
CREATE TABLE KeyArchiveIntegrity (
    Kai_Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Kai_SizeBytes    INTEGER NOT NULL,
    Kai_Sha256Hex    TEXT    NOT NULL,
    Kai_RecordedUtc  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);

CREATE TABLE DbVersion (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Version     TEXT    NOT NULL,
    AppliedOn   TEXT    NOT NULL,
    Description TEXT,
    IsCurrent   INTEGER NOT NULL CHECK (IsCurrent IN (0, 1))
);

/* =========================
   NEW TABLE: LogMessageTemplate
   (Add this CREATE TABLE block in your CREATE TABLE area)
   ========================= */
CREATE TABLE LogMessageTemplate (
    LMT_Id      INTEGER PRIMARY KEY AUTOINCREMENT,
    UpdateForm  TEXT    NOT NULL,   -- e.g., 'BasicTab'
    Seq         INTEGER NOT NULL,   -- display/order index
    LogMessage  TEXT    NOT NULL,   -- template text with tokens like #CategoryItemName#
    Active      INTEGER NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc  TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc  TEXT    NOT NULL DEFAULT (datetime('now')),
    UNIQUE (UpdateForm, Seq)
);

-- Optional but sensible: quick lookup by form, ordered by Seq
CREATE INDEX IF NOT EXISTS IX_LogMessageTemplate_Form_Seq
ON LogMessageTemplate (UpdateForm, Seq);

/* =========================
   SEED: LogMessageTemplate (idempotent)
   (Add this in your SEEDS transaction)
   ========================= */
-- Seed / upsert BasicTab templates (no tabs/newlines stored in DB; formatting happens in code)
INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    -- NEW ITEM ONLY
    SELECT 'BasicTab' AS UpdateForm, 1 AS Seq,
           'Category Item #CategoryItemName# has been created for Category #CategoryName#' AS LogMessage

    -- EDIT EXISTING ITEM (saved changes; value-blind; no special chars stored)
    UNION ALL SELECT 'BasicTab', 2,  'The following updates have been saved for #CategoryItemName#'
    UNION ALL SELECT 'BasicTab', 3,  '- Password updated'
    UNION ALL SELECT 'BasicTab', 4,  '- Bookmark flag toggled'
    UNION ALL SELECT 'BasicTab', 5,  '- PIN updated'
    UNION ALL SELECT 'BasicTab', 6,  '- User name updated'
    UNION ALL SELECT 'BasicTab', 7,  '- URL/Location updated'
    UNION ALL SELECT 'BasicTab', 8,  '- Phone number updated'
    UNION ALL SELECT 'BasicTab', 9,  '- Email updated'
    UNION ALL SELECT 'BasicTab', 10, '- Notes updated'

    -- OPTIONAL: explicit discard/revert path (only if we wire a "Discard" action)
    UNION ALL SELECT 'BasicTab', 11, 'Edits were discarded for #CategoryItemName# (no changes saved)'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);


CREATE TABLE Logs (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    WhenUtc       TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CreatedUtc    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    Level         TEXT    NOT NULL CHECK (UPPER(Level) IN ('TRACE','DEBUG','INFO','WARN','WARNING','ERROR','FATAL')),
    Source        TEXT,
    EventCode     TEXT,
    SessionId     TEXT    NOT NULL DEFAULT '',
    LoginId       TEXT,
    ItemId        INTEGER,

    -- NEW: non-sensitive “who/what this log is about” (Category or CategoryItem name)
    SubjectText   TEXT,

    -- NEW: rendered multi-line message built from templates (no payload)
    MessageText   TEXT,

    MachineId     TEXT,
    DeviceMake    TEXT,
    DeviceModel   TEXT,
    OSVersion     TEXT,
    DeviceIdHash  TEXT,
    InstallType   TEXT,
    AppVersion    TEXT    NOT NULL DEFAULT '',
    IsCrash       INTEGER NOT NULL DEFAULT 0 CHECK (IsCrash IN (0,1)),
    KeySetVersion INTEGER NOT NULL DEFAULT 1,
    StackHash     TEXT
);


COMMIT;

-- =========================
-- SEEDS (idempotent)
-- =========================
BEGIN TRANSACTION;

/* =========================================================
   Combo: account_types  (bucket for common account kinds)
   Used by CategoryItemAccounts.CIA_AccountType (optional)
   ========================================================= */

/* Ensure ComboType: account_types */
INSERT INTO ComboType (Code, Description, Active)
SELECT 'account_types', 'Common financial account types', 1
WHERE NOT EXISTS (SELECT 1 FROM ComboType WHERE Code = 'account_types');

/* Ensure ComboType: account_types */
INSERT INTO ComboType (Code, Description, Active)
SELECT 'account_types', 'Common financial account types', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'account_types'
);
/* Ensure ComboDetail rows for account_types (includes Primary account) */
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
JOIN (
    SELECT 0 AS Seq, 'PRIMARY'       AS Code, 'Primary account'        AS Description
    UNION ALL SELECT 1, 'CHECKING',      'Checking'
    UNION ALL SELECT 2, 'SAVINGS',       'Savings'
    UNION ALL SELECT 3, 'CHRISTMAS',     'Christmas Club'
    UNION ALL SELECT 4, 'MONEY_MARKET',  'Money Market'
    UNION ALL SELECT 5, 'IRA',           'IRA'
    UNION ALL SELECT 6, 'RETIREMENT',    'Retirement'
    UNION ALL SELECT 7, '401K',          '401(k)'
    UNION ALL SELECT 8, 'OTHER',         'Other (freeform)'
) AS v
WHERE ct.Code = 'account_types'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId
        AND cd.Code        = v.Code
  );

/* ComboType: credit_cards (mixed: types + brands) */
INSERT INTO ComboType (Code, Description, Active)
SELECT 'credit_cards', 'Credit/Bank card options (mixed)', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'credit_cards'
);

/* Ensure ComboDetail rows for credit_cards */
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
JOIN (
    SELECT 0 AS Seq, 'DEBIT_CARD'     AS Code, 'Debit card'                    AS Description
    UNION ALL SELECT 1, 'MASTERCARD',      'Mastercard'
    UNION ALL SELECT 2, 'VISA',            'Visa'
    UNION ALL SELECT 3, 'AMERICAN_EXPRESS','American Express'
    UNION ALL SELECT 4, 'DISCOVER',        'Discover'
    UNION ALL SELECT 5, 'STORE_CARD',      'Store card (private label)'
    UNION ALL SELECT 6, 'VIRTUAL_CARD',    'Virtual card'
) AS v
WHERE ct.Code = 'credit_cards'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId
        AND cd.Code        = v.Code
  );

/* NOTE: category_types removed – Category_Type now default 0 with no FK */

/* *** log_filters for Logs UI *** */

/* Ensure ComboType: log_filters */
INSERT INTO ComboType (Code, Description, Active)
SELECT 'log_filters', 'Filters for the Logs UI', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'log_filters'
);

/* Ensure ComboDetail rows for log_filters (idempotent) */
WITH v(Seq, Code, Description) AS (
    VALUES
      ( 0, 'CATEGORY_DUPLICATE',        'Duplicate category detected'),
      ( 1, 'CATEGORY_INSERTED',         'Category successfully inserted'),
      ( 2, 'LOGIN',                     'Login events'),
      ( 3, 'EARLY_FAIL',                'Early-fail events'),
      ( 4, 'SESSION_START',             'Session started (post-login)'),
      ( 5, 'SESSION_END',               'Session ended'),

      (10, 'CATEGORYITEM_NAME_ADDED',        'Category item created (name set)'),
      (11, 'CATEGORYITEM_NAME_CHANGED',      'Category item name changed'),
      (12, 'CATEGORYITEM_PASSWORD_CHANGED',  'Category item password changed'),
      (13, 'CATEGORYITEM_PIN_CHANGED',       'Category item PIN changed'),
      (14, 'CATEGORYITEM_EMAIL_CHANGED',     'Category item email changed'),
      (15, 'CATEGORYITEM_URL_CHANGED',       'Category item URL changed'),
      (16, 'CATEGORYITEM_PHONE_CHANGED',     'Category item phone number changed'),
      (17, 'CATEGORYITEM_NOTES_CHANGED',     'Category item notes changed')
)
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
CROSS JOIN v
WHERE ct.Code = 'log_filters'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail d
      WHERE d.ComboTypeId = ct.ComboTypeId
        AND d.Code        = v.Code
  );


/* Starter Categories (now independent of ComboDetail / category_types) */
INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Utilities', 'Bills and essential services', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Utilities');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Government', 'Government portals & services', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Government');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Banks', 'Banks & credit unions', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Banks');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Shopping', 'Retail & e-commerce', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Shopping');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Entertainment', 'Streaming & media', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Entertainment');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Healthcare', 'Health & medical portals', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Healthcare');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Insurance', 'Insurance accounts', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Insurance');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Social', 'Social networks & messaging', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Social');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Email', 'Email providers & identity', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Email');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Cloud', 'Cloud & hosting', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Cloud');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Development', 'Dev tools & repos', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Development');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Education', 'Schools, courses, LMS', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Education');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Travel', 'Airlines, hotels, transport', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Travel');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Misc', 'Everything else', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Misc');

/* ComboType: basic_change_fields (Basic tab field-change descriptors) */
INSERT INTO ComboType (Code, Description, Active)
SELECT 'basic_change_fields', 'Basic tab: changed field descriptors', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'basic_change_fields'
);

/* Ensure ComboDetail rows for basic_change_fields */
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
JOIN (
    SELECT 0  AS Seq, 'BASIC_ITEM_NAME'    AS Code, 'Category Item Name changed'     AS Description
    UNION ALL SELECT 1,  'BASIC_PASSWORD',       'Password changed'
    UNION ALL SELECT 2,  'BASIC_PIN',            'PIN changed'
    UNION ALL SELECT 3,  'BASIC_USERNAME',       'User Name changed'
    UNION ALL SELECT 4,  'BASIC_URL',            'URL or Absolute Location changed'
    UNION ALL SELECT 5,  'BASIC_PHONE',          'Phone Number changed'
    UNION ALL SELECT 6,  'BASIC_EMAIL',          'Email changed'
    UNION ALL SELECT 7,  'BASIC_NOTES',          'Freeform Notes changed'
    UNION ALL SELECT 8,  'BASIC_BOOKMARK_ONLY',  'Bookmark-only setting changed'
) AS v
WHERE ct.Code = 'basic_change_fields'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId
        AND cd.Code        = v.Code
  );


COMMIT;
