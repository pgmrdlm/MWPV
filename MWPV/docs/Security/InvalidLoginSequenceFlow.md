# Invalid Login Sequence

## Legend
- **U** – User (person at keyboard)
- **W** – AppEntryWindow / PasswordEntryWindow (login/setup UI)
- **C** – UICleaner (wipes WPF PasswordBox/TextBox)
- **K** – KeyProvisioner (loads/derives keys from encrypted keyfile)
- **S** – SecureEncryptedDataStore (in-memory session key store)
- **DB** – SQLCipher DB (encrypted SQLite)
- **FS** – Filesystem (keyfile location)
- **E** – EarlyLoginFailures (DPAPI-protected *.elogp* files)

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
  participant C as UICleaner
  participant FS as Filesystem (keyfile)

  %% USER INPUT
  U->>W: Enter password (char[]) + select keyfile

  %% LOAD / VERIFY / DERIVE
  W->>K: Load keyfile + derive keys using supplied password
  alt Keyfile integrity or format invalid
    K-->>W: Verify failed
    W->>E: Write early failure (.elogp: KeyFileVerifyError, DPAPI-protected)
  else Derivation ok → attempt DB open
    K-->>S: Stage keys into SecureEncryptedDataStore (DbPassword, LogPayloadKey, UserSecretsKey)
    W->>DB: PRAGMA key = DbPassword (open)
    alt SQLCipher cannot decrypt/auth page 0
      DB-->>W: Auth failed
      W->>E: Write early failure (.elogp: InvalidPasswordOrKeyFile, DPAPI-protected)
      S-->>S: Clear staged keys (no secrets retained)
    else Success (not this flow)
    end
  end

  %% USER FEEDBACK + UI WIPES
  W-->>U: Error dialog: invalid credentials / keyfile
  W->>C: Wipe UI buffers (PasswordBox/TextBox), temp arrays
  C-->>W: Buffers zeroized

  note right of W: Early failure saved (DPAPI). On next successful login:<br/>• Ingest .elogp into encrypted log DB (AES-GCM)<br/>• Notify user non-blocking<br/>• Securely delete ingested .elogp source files
