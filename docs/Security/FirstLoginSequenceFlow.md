# First-time Login Sequence

## Legend
- **U** – User (person at keyboard)
- **W** – AppEntryWindow (the login/setup UI window)
- **C** – UICleaner (wipes textboxes, char[] buffers)
- **K** – KeyProvisioner (creates/loads keyset.json inside encrypted keyfile)
- **S** – SecureEncryptedDataStore (holds session keys in memory)
- **DB** – SQLCipher DB (encrypted SQLite database)
- **FS** – Filesystem (where keyfile lives)
- **E** – EarlyLoginFailures (DPAPI-protected early .elogp files)

## Diagram
```mermaid
sequenceDiagram
  autonumber
  participant U as User
  participant W as AppEntryWindow
  participant C as UICleaner
  participant K as KeyProvisioner
  participant S as SecureEncryptedDataStore
  participant DB as SQLCipher DB
  participant FS as Filesystem (keyfile)

  %% CREDENTIALS: user chooses password + keyfile location
  U->>W: Enter master password (char[]) + choose keyfile path
  W->>K: Pass password (char[]) + keyfile path

  %% KEY PROVISIONING
  activate K
  K->>K: Generate salts + nonces
  K->>K: Derive KDF material from password (no strings)
  K->>K: Create keyset {DbPassword, LogPayloadKey, UserSecretsKey, KeySetVersion}
  K->>FS: Write encrypted keyfile (7z/JSON or similar)
  K-->>S: Load keys into SecureEncryptedDataStore (session only)
  deactivate K

  %% DB CREATION + SCHEMA BOOTSTRAP
  W->>DB: Create SQLCipher database (PRAGMA key = DbPassword)
  DB-->>W: DB file created
  W->>DB: Bootstrap schema (tables, indexes, logs schema)

  %% IMMEDIATE WIPES (no plaintext left behind)
  W->>C: Request wipe of password textbox buffer
  C-->>W: Textbox buffer zeroized
  K->>C: Request wipe of temp keyfile bytes + derived arrays
  C-->>K: Buffers zeroized

  %% PERSISTENCE + READY
  S-->>W: Session keys available (no strings)
  W-->>U: Setup complete → proceed to MainWindow
```
