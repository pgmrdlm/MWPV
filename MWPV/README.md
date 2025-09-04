# MWPV – My Windows Password Vault

## Description
MWPV is a hardcore security password vault for Windows, designed so normal users get serious protection without wrestling with settings all day. The only time you directly touch the security guts is at **login**.

At login you provide:
- **A password**, and
- **A password-protected key file** (you choose where it lives)

That key file isn’t a config; it’s the heart of your protection. Move it, rename it, hide it—MWPV won’t open without **both the file and its password**.

After login, protection is **defense‑in‑depth**: an attacker generally needs **both** the key file *and* its password to read the vault; and (once column‑level encryption is enabled) certain fields remain separately protected. **If the device is compromised while unlocked, or both the key file and password are exposed, all bets are off.**

## What’s implemented today
- **Key file–based unlock** (user-chosen file + password)
- **Database encryption at rest** via SQLCipher (AES-256)
- **Early login failure capture** (DPAPI `.elogp`), **ingested on next successful login** with notification and **secure deletion** of the source files
- **Secure memory handling** (**in‑memory encryption** for retained secrets, wiping of transient plaintext; UI fields cleared via `UICleaner`)
- **Offline by default** (no background network calls; manual “Check for Updates” only)

> For design details and diagrams, see **`docs/Security/Security_Documentation.md`**.

## Planned (near-term)
- **Password strength meter** in the login UI (warning-only guidance)
- **Log Viewer UI** (filters, drill-in; shows changed columns; ingest banner)
- **Column-level encryption** for user-entered sensitive fields using a **separate, randomly generated `UserSecretsKey`** (AES-256-GCM per protected column)
- Purge/retention policy for logs (e.g., 90 days)

## Security Architecture (high level)

### 1) Login & Key File
- Key file is an encrypted archive (7-Zip) containing a JSON keyset.
- Keys inside include:
  - **`DbPassword`** – opens the SQLCipher database.
  - **`LogPayloadKey`** – encrypts/decrypts log payloads (AES-GCM).
  - **`UserSecretsKey`** – reserved for column-level encryption (planned).
- The key file is unlocked by your password (KDF-derived), then keys are staged **in memory** for the session and wiped when not needed.

### 2) Database Encryption
- Vault data is stored in an **AES-256 encrypted SQLite (SQLCipher)** database.
- The database key is never persisted in plaintext and is only present in memory after a successful login.

### 3) Logging & Early Failures
- **Before DB unlock:** invalid login attempts are written to **DPAPI-protected** `.elogp` files.
- **After a successful login:** `.elogp` files are **ingested into the encrypted log tables**, the user gets a **non-blocking notification**, and the original `.elogp` files are **securely deleted**.
- Log payloads are **AES-GCM** encrypted using `LogPayloadKey`.

### 4) Secure Memory Handling
- Outside of active cryptographic operations, **sensitive values are encrypted in memory** under a per‑session key; plaintext exists only transiently and is **zeroized immediately** after use.
- UI secrets (e.g., WPF `PasswordBox`/`TextBox`) are cleared via `UICleaner` immediately after use.

### 5) Offline-First
- No background network activity. Only user-initiated update checks go online.

## Disaster Recovery
1. Back up your **encrypted database** and **encrypted key file**.
2. Install MWPV on the new system.
3. Copy the database and key file over.
4. Launch MWPV, select your key file, enter its password—everything’s right where you left it.

## Security Summary
- **Key File Protection** – Encrypted archive holding cryptographic keys (password-protected, user-controlled location).
- **Database Encryption** – Entire vault encrypted with SQLCipher (AES-256).
- **Logging** – AES-GCM encrypted payloads; DPAPI-protected early login failures ingested on next success.
- **Secure Memory** – **In‑memory encryption** for retained secrets, plus immediate wiping of transient plaintext; UI fields cleared.
- **Offline Operation** – No background network access.

## Roadmap Notes
- **Column-level encryption**: protected columns will be individually encrypted with **AES-256-GCM**, using a **separate `UserSecretsKey`** (randomly generated at setup and stored only in the encrypted key file).
- **Password strength meter**: guidance only; policy enforced at submit (min 8 chars; at least 2 of 3: upper/lower/special).
- **Log Viewer**: full-panel view with filters, drill-in, and ingest banner.

## Third-Party Credits
- **7-Zip** — Copyright © 1999–2025 Igor Pavlov  
  Website: https://www.7-zip.org/  
- **SQLCipher** — SQLite extension for transparent encryption  
  Website: https://www.zetetic.net/sqlcipher/

---

**This isn’t security theater.** It’s layered, reviewable, and blunt about how it works—because it can be. If you feel you have found any security holes, please tell me 
so that they can be corrected.  The whole point of this vault is to be as secure as possible.
