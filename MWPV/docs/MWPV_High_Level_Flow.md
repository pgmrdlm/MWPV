# MWPV High-Level Architecture and Flow

## Purpose and Scope

This document gives a deliberately broad view of the MWPV WPF application's authenticated startup, runtime, upgrade, backup, and shutdown responsibilities. It reflects the current solution structure and runtime paths; it is not a class or method-level design.

## System Context

```mermaid
flowchart LR
    User[User]
    Installer["MWPV Installer<br/>installation and upgrade launch marker"]

    subgraph Windows["Windows / user environment"]
        WPF["MWPV WPF application<br/>main executable"]
        Clipboard["Windows clipboard"]
        DPAPI["Windows DPAPI<br/>current user"]
        Local["Local application-data filesystem"]
        Docs["User documents filesystem"]
    end

    subgraph Libraries["Supporting projects"]
        Security["Security.Utility DLL<br/>crypto, validation, protected runtime storage,<br/>wiping, secure deletion, result types"]
        Backup["Backup.Utility DLL<br/>backup creation, verification, manifests, retention"]
        Catalog["MWPV.SqlCatalog project<br/>trusted SQL names, hashes, upgrade routes"]
        KeyLogic["KeyFileLogic project<br/>SQLCipher key-file access"]
    end

    subgraph ProtectedData["Protected local data"]
        KeyFile["Encrypted SQLite key-file database<br/>keyset and trusted SQL payload"]
        Vault["SQLCipher password database<br/>vault data, settings, encrypted audit logs"]
        Early["Early-login .elogp files<br/>DPAPI-protected"]
        SqlFiles["Trusted SQL staging files"]
        RuntimeSql["Verified runtime SQL store<br/>in protected process memory"]
        Secrets["Protected runtime secret storage<br/>keys and database password"]
        BackupFolders["Exit and upgrade backup folders<br/>files plus manifest"]
    end

    User --> WPF
    Installer -->|installs or launches upgrade mode| WPF
    WPF --> Security
    WPF --> Backup
    WPF --> Catalog
    WPF --> KeyLogic
    KeyLogic --> KeyFile
    WPF --> Vault
    WPF --> Early
    Early --> DPAPI
    WPF --> SqlFiles
    Catalog -->|validates hashes and route| SqlFiles
    WPF --> RuntimeSql
    WPF --> Secrets
    WPF --> Clipboard
    Backup --> BackupFolders
    Installer -->|backs up application files during update| Docs
    Local --- Vault
    Local --- Early
    Local --- SqlFiles
    Local --- BackupFolders
```

## Startup and Normal Runtime Flow

```mermaid
flowchart TD
    Launch["User launches MWPV"] --> Detect["MWPV determines startup mode<br/>normal, fresh install, or upgrade"]
    Detect --> Entry["WPF entry window<br/>collects vault password and key-file path"]

    Entry --> KeyCheck["Validate encrypted SQLite key-file<br/>password, schema, and payload"]
    KeyCheck -->|valid| LoadKeys["Load keyset material into<br/>protected runtime secret storage"]
    LoadKeys --> SqlVerify["Validate trusted SQL payload<br/>against MWPV.SqlCatalog hashes"]
    SqlVerify --> SqlStore["Place verified SQL in<br/>runtime SQL store"]
    SqlStore --> OpenVault["Open SQLCipher password database<br/>using loaded database password"]
    OpenVault --> SessionStart["Write session-start log"]
    SessionStart --> Ingest["Ingest pending early-login .elogp files<br/>into the audit log when present"]
    Ingest --> Main["Main WPF interface"]

    subgraph Runtime["Authenticated application operation"]
        Main --> Services["Application services<br/>categories, saved items, settings, logs, exports"]
        Services --> RuntimeSql
        Services --> Vault["SQLCipher password database<br/>vault data and audit logs"]
        Services --> Secrets["Protected runtime secret storage"]
    end

    Early["Early-login .elogp files<br/>DPAPI-protected"] --> Ingest
    SqlPayload["Trusted SQL from encrypted key-file payload"] --> SqlVerify
    Security["Security.Utility<br/>validation, cryptography, protected storage, wiping"] -.supports.-> KeyCheck
    Security -.supports.-> LoadKeys
```

## Upgrade, Backup, and Shutdown Flow

```mermaid
flowchart TD
    Start["MWPV launch"] --> Mode{"Upgrade mode?"}
    Mode -->|yes| Login["Authenticated key-file and database login"]
    Login --> UpSql["Validate upgrade SQL<br/>and determine route"]
    UpSql --> UpBackup["Backup.Utility creates and verifies<br/>full upgrade backup with manifest"]
    UpBackup --> UpgradeDb["Apply and validate database upgrade"]
    UpgradeDb --> RewriteKey["Rewrite and validate key-file<br/>trusted SQL payload"]
    RewriteKey --> Publish["Publish verified runtime SQL,<br/>securely scrub staged SQL when possible,<br/>clear upgrade flag"]
    Publish --> Normal["Normal authenticated operation"]

    Mode -->|no| Normal
    Installer["Installer"] -->|installation or migration launch marker| Start
    UpBackup --> UpgradeStore["upgrade-backups<br/>backup files and manifest"]

    Normal --> Close{"User closes MWPV"}
    Close --> EndLog["Write session-end log"]
    EndLog --> BackupChoice{"Create exit backup?"}
    BackupChoice -->|yes| ExitBackup["Checkpoint database; Backup.Utility<br/>creates, verifies, and retains exit backup"]
    BackupChoice -->|no| Cleanup
    ExitBackup --> Cleanup["Clear owned clipboard data;<br/>wipe sensitive runtime state; close"]
    Cleanup --> Exit["Application exits"]

    ExitBackup --> ExitStore["backups<br/>backup files and manifest"]
    Security["Security.Utility"] -.wiping and secure deletion.-> Cleanup
    Backup["Backup.Utility"] -.verification and retention.-> UpBackup
    Backup -.verification and retention.-> ExitBackup
```

## Component Responsibilities

| Component | High-level responsibility |
|---|---|
| MWPV | WPF entry, startup-mode selection, authenticated runtime, application services, session logging, and shutdown coordination. |
| Security.Utility | Cryptographic helpers, validation results, protected in-process secret storage, sensitive-data wiping, and secure-deletion support. |
| Backup.Utility | Creates and verifies backup sets, writes and verifies manifests, restores where invoked, and applies retention. |
| SQL catalog/runtime SQL components | `MWPV.SqlCatalog` defines trusted SQL hashes and upgrade routes; MWPV validates SQL and exposes only verified SQL through the runtime store. |
| Password database | SQLCipher `MWPV.db` contains vault application data, settings, and audit logs. |
| Key-file database | Encrypted SQLite key-file database stores the keyset payload, including the database password, runtime keys, and trusted SQL payload. |
| Logging | Session and audit logs are written through the application log service to the password database; pre-authentication failures are DPAPI-protected `.elogp` files and are ingested after successful login. |
| Installer | Installs the published application and, for an update migration, launches MWPV with the migration marker that selects upgrade mode. |

## Important Boundaries

- **Password and key-file authentication boundary:** the entry flow validates the encrypted key-file password, schema, and payload before using its keyset to open the password database.
- **Trusted SQL integrity boundary:** SQL is accepted only after `MWPV.SqlCatalog` validates the required files and SHA-256 hashes; the runtime store receives the verified payload.
- **DLL responsibility boundaries:** MWPV owns application workflow and UI; `Security.Utility` owns reusable security primitives and cleanup; `Backup.Utility` owns backup-set creation, verification, manifests, and retention.
- **Database and filesystem boundary:** the SQLCipher password database, key-file database, early-login files, SQL staging, and backup folders are filesystem-backed; secret material used during a session is held in protected runtime storage.
- **Backup verification and publication boundary:** upgrade and exit backups are created before they are treated as usable; manifests and file hashes are verified, and retention is applied to exit backups.
- **Shutdown cleanup boundary:** close handling writes the session-end log, may complete a verified exit backup, clears clipboard data owned by MWPV, and performs best-effort sensitive-runtime cleanup before exit.

## Deliberate Omissions

This document intentionally omits class-level design, database schemas, individual screens, individual SQL scripts, and detailed exception paths.
