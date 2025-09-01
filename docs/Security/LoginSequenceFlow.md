# Normal Login Sequence

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

    %% CREDENTIALS ENTRY
    U->>W: Enter password + select key file
    W->>K: Pass credentials (char[] + key file path)

    %% STORE KEYS
    K->>S: Store DbPassword, LogPayloadKey, UserSecretsKey
    S-->>K: Keys staged (no strings)

    %% IMMEDIATE WIPES
    W->>C: Request wipe of password textbox buffer
    C->>W: Textbox buffer zeroized
    K->>C: Request wipe of temp keyfile bytes + derived arrays
    C->>K: Buffers zeroized

    %% UNLOCK DB
    K->>S: Get DbPassword (read-only span)
    K->>DB: PRAGMA key = DbPassword
    DB-->>K: Database unlocked

    %% CLEANUP + ACCESS
    K->>C: Request wipe of any remaining temps
    W-->>U: Access granted (MainWindow shown)

    %% SHUTDOWN GUARANTEE
    Note over S: "SEDS holds runtime keys only. Dispose wipes on shutdown."
```
