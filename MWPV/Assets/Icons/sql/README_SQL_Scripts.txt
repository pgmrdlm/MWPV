MWPV Logging SQL Scripts (v2025-08-13)
=====================================

Files:
  - Logs_Insert_V2.sql
      Insert template for the current log schema. Bind all @params shown.

  - Logs_Select_Recent.sql
      Select recent entries; bind @Limit, and optionally @CrashesOnly (1/0/NULL).

  - Logs_Init.sql
      CREATE TABLE IF NOT EXISTS for the Logs table, plus useful indexes.

  - Logs_Indexes.sql
      Idempotent index creation; safe to run anytime.

SecureEncryptedDataStore keys (recommended):
  - "Logs_Insert_V2.sql"
  - "Logs_Select_Recent.sql"
  - "Logs_Init.sql"
  - "Logs_Indexes.sql"

Suggested loader:
  For each .sql in your key archive (e.g., /Keys/Sql/*.sql), load the text into
  SecureEncryptedDataStore using the exact filename as the key, e.g.:
      SecureEncryptedDataStore.Set("Logs_Insert_V2.sql", <fileText>);

Notes:
  - Keep runtime PRAGMA settings (journal_mode, synchronous) in code where the DB is opened.
  - If you later change columns, bump PayloadVer/KeySetVersion and ship a migration script.
  - Legacy templates (WhenUtc/Category/Message) are *not* included; standardize on v2.
