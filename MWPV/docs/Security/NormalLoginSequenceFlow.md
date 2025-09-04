# Normal Login Sequence

## Legend
- **U** – User (person at keyboard)
- **W** – AppEntryWindow / PasswordEntryWindow (login/setup UI)
- **C** – UICleaner (wipes WPF PasswordBox/TextBox)
- **K** – KeyProvisioner (loads/derives keys from encrypted keyfile)
- **S** – SecureEncryptedDataStore (in-memory session key store)
- **DB** – SQLCipher DB (encrypted SQLite)
- **E** – EarlyLoginFailures (DPAPI-protected *.elogp* files)
- **L** – Encrypted Log DB (SQLCipher; AES-GCM payloads)
- **FS** – Filesystem (keyfile location)

## Diagram
```mermaid
sequenceDiagram
  autonumber
  participant U as User
  participant W as AppEntryWindow
  participant K as KeyProvisioner
  participant S as SecureEncryptedDataStore
  participant DB as SQLCipher DB
  participant E as EarlyLoginFailures (DPAPI .elogp)
  participant L as Encrypted Log DB
  participant C as UICleaner
  participant FS as Filesystem (keyfile)

  %% USER INPUT
  U->>W: Enter password (char[]) + select keyfile

  %% KEYFILE / DERIVATION
  W->>K: Load keyfile + derive keys using supplied password
  K-->>S: Stage keys (DbPassword, LogPayloadKey, UserSecretsKey)

  %% OPEN DATABASE
  W->>DB: PRAGMA key = DbPassword (open)
  DB-->>W: Open OK

  %% INGEST ANY PRIOR EARLY FAILURES (only if present)
  W->>E: Scan for .elogp files
  alt Any found?
    E-->>W: List of .elogp files
    loop each .elogp
      W->>L: Ingest parsed entry (AES-GCM payload with LogPayloadKey)
    end
    W-->>U: Non-blocking notice: previous invalid login attempts were recorded & ingested
    W->>E: Securely delete ingested .elogp source files
  else
    E-->>W: None found
  end

  %% UI CLEANUP
  W->>C: Wipe UI buffers (PasswordBox/TextBox)
  C-->>W: Buffers zeroized

  %% SUCCESS HANDOFF
  W-->>U: Proceed to MainWindow
