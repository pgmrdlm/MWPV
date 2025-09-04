# MWPV – Developer & Modification Guide

This document defines the rules, standards, and expectations for anyone who wishes to modify, contribute to, or extend **MWPV – My Windows Password Vault**.

It exists to preserve the security, integrity, and design principles that make MWPV a hardcore security application for normal users.

---

## 1. Core Security Principles

All modifications must respect the following non-negotiable security pillars:
- **Multi-layer encryption** – Database, field-level, and memory handling must remain intact.
- **Secure key management** – All cryptographic keys must be generated, stored, and loaded only through the centralized key provisioning system.
- **No silent weakening** – No changes should reduce encryption strength, remove secure memory wiping, or introduce unnecessary network activity.

---

## 2. Code Attribution Rules

- Every source file must retain the original file header:
```csharp
// MWPV – My Windows Password Vault
// Author: Daniel Miller – https://DansGeekStop.com
// Licensed under MIT (see LICENSE)
```
- The LICENSE file must remain in the repository.
- Any distribution of modified code must include a **Changes from upstream** section in its README or release notes.

---

## 3. Password Handling

- **User-generated passwords** must be stored as `char[]` and securely wiped after use.
- **Machine-generated passwords** must be created using a cryptographically secure RNG.
- No plaintext passwords may be written to disk or logs at any time.

---

## 4. Key Management

- All cryptographic keys must be created and stored in the encrypted key file.
- Keys must only be loaded into memory when needed and must be securely wiped immediately after use.
- Keys may not be hardcoded into the application.

---

## 5. Error Handling

- No stray message boxes or inline `MessageBox.Show` calls for errors.
- All errors must be routed through the centralized helper method for uniform handling and logging.
- Pre-login errors must be logged to DPAPI-protected early log files, then ingested into the database upon the next successful login.

---

## 6. Logging

- Logs must be stored in encrypted JSON format.
- Log encryption keys must be machine-bound where applicable.
- Temporary log files must be securely deleted after ingestion.

---

## 7. Shutdown & Cleanup

- Sensitive data must be wiped from memory during both normal and abnormal shutdowns.
- Wipe routines must overwrite variables before releasing them.

---

## 8. SQL Storage

- All SQL scripts must be stored in an encrypted archive.
- SQL must be loaded into `SecureEncryptedDataStore` at runtime.

---

## 9. Backup & Restore

- Backup and restore functionality must follow the established secure process.
- Backups must remain encrypted at all times.

---

## 10. Third-Party Dependencies

MWPV uses the following third-party software:
- **7-Zip** — Copyright © 1999–2025 Igor Pavlov  
  - Website: https://www.7-zip.org/  
  - License: GNU LGPL

Any additional dependencies must be reviewed for licensing compatibility and security impact.

---

## Final Note

MWPV’s design is deliberate and security-first. Any contributions or modifications must preserve the high security standards outlined here. If you are unsure whether a change meets these requirements, open an issue or contact the project maintainer before proceeding.
