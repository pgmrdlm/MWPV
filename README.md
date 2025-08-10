# MWPV – My Windows Password Vault

## Description
MWPV is a hardcore security password vault for Windows, designed so normal users can benefit from advanced protection without constantly wrestling with security settings. The only time you directly interact with security is during login.

- The one extra step I’ve added is choosing a password-protected file, stored anywhere you like, that contains the **cryptographic keys** required to unlock your data. This isn’t a simple config file — this is the heart of the vault’s protection. You can move it, rename it, or hide it anywhere you want. At login, you point MWPV to that file and provide the password. Without both, the vault stays locked.

After login, the protection is invisible but relentless. MWPV is built on multiple independent security layers so that if one layer is ever breached, the others still stand. These include:
- Database encryption at rest using AES-256 via SQLCipher.
- Field-level encryption for individual sensitive items (passwords, security answers), each encrypted with a separate key.
- Secure memory wiping for decrypted data — no sensitive value lingers in memory after it’s used.
- Offline by default — no background network connections. The only online action is a user-initiated “Check for Updates.”

## Disaster Recovery
If your computer is replaced, wiped, or upgraded, restoring MWPV is quick and safe:
1. Back up your encrypted database and your encrypted key file.
2. Install MWPV on the new system.
3. Copy your database and key file over.
4. Log in with your existing key file and password — everything is exactly where you left it.

## Security Summary
- Key File Protection – AES-256 encrypted file containing all cryptographic keys. Password-protected and user-controlled location.
- Database Encryption – Entire vault encrypted at rest with SQLCipher AES-256.
- Field-Level Encryption – Each sensitive entry encrypted with a dedicated key.
- Secure Memory Handling – Sensitive values stored in `char[]`/`byte[]`, overwritten immediately after use.
- Offline Operation – No network activity except user-initiated updates.

## Security Architecture
MWPV’s design assumes that a determined attacker may gain access to your device or database file — and still prevents them from reading your data without both the key file and its password.

### 1. Login & Key File
- Key file encrypted with AES-256, unlocked only by the password you provide at login.
- Stores multiple cryptographic keys:
  - Database key – decrypts the SQLCipher-encrypted vault.
  - Field-level key – encrypts/decrypts individual sensitive entries.
  - Future keys – reserved for potential features like encrypted logs or user-provided files.
- File location never stored in the application — you decide where it lives.

### 2. Database Encryption
- Vault stored in an AES-256 encrypted SQLite database (SQLCipher).
- Encryption key loaded into memory only after successful login and wiped when not needed.

### 3. Field-Level Encryption
- Even if the database encryption key is somehow compromised, individual sensitive fields remain encrypted with a different key from the key file.
- Protects against partial data exposure.

### 4. Secure Memory Handling
- Sensitive values never stored in immutable strings.
- Secure overwrite routines ensure decrypted values are erased from memory after use.

### 5. Offline-Only Operation
- No network requests are made by the application except for manual update checks.
- Eliminates the risk of remote exploitation via network services.

## Final Word
This is not “security theater.” This is a transparent, layered, open-to-review design that doesn’t hide how it works because it doesn’t need to.
If you think you can break it — go ahead and try.

---

## Third-Party Credits
MWPV uses the following third-party software:

- **7-Zip** — Copyright © 1999–2025 Igor Pavlov  
  - Website: [https://www.7-zip.org/](https://www.7-zip.org/)  
  - License: GNU LGPL  
