# MWPV — My Windows Password Vault

MWPV is a Windows desktop password vault designed for personal use. It is built with C# and WPF on .NET 8 and uses an encrypted SQLite database for local storage.

The project is currently under active development and is not yet ready for general release.

## Project Goals

MWPV is intended to provide:

- Local password and account information storage
- An encrypted application database
- User-controlled backups and retention
- Secure password generation
- Categories for organizing stored items
- Security questions and other account-related information
- Operational logging without storing sensitive vault contents
- Portable and standard Windows installation options
- Controlled and verified database upgrades

MWPV does not depend on cloud storage for normal operation.

## Security Design

MWPV uses several layers of protection, including:

- SQLCipher-encrypted SQLite storage
- A separate encrypted key-file database
- Runtime-only access to decrypted secrets
- AES-256 encryption for selected application data
- AES-GCM encrypted logging payloads
- Automatic clearing of sensitive clipboard contents
- Password-policy enforcement
- Verification of trusted SQL files before installation or database upgrades
- Verified backups with SHA-256 file hashes and manifests

No security design should be treated as infallible. The source code is published so that the design and implementation can be reviewed.

## Repository Structure

Major repository areas include:

- `MWPV/` — Main WPF application
- `Security.Utility/` — Shared security-related functionality
- `Backup.Utility/` — Backup creation, verification, and retention
- `MWPV.SqlCatalog/` — Trusted SQL catalog and package validation
- `MWPV.SqlCatalog.Tests/` — SQL catalog validation tests
- `Installer/` — Inno Setup installer project
- `MWPV/docs/` — Architecture, flow, security, and application documentation

## Documentation

Project documentation is being expanded in preparation for release.

Current and planned documentation includes:

- High-level application flows
- Component responsibilities
- Trust boundaries
- Database and SQL upgrade handling
- Backup and restore behavior
- Logging design
- Security decisions
- Installation instructions
- User help documentation

Some documents may be provided in both Markdown and HTML formats for easier viewing.

## Current Development Status

MWPV remains a work in progress.

Current development work includes:

- Security review and cleanup
- SQL catalog verification
- Backup and upgrade validation
- Logging review and retention behavior
- Application flow documentation
- User help screens
- Installer and release preparation

The current database development baseline is version `01.23`.

No production release should be assumed from the presence of source code, tags, branches, installers, or documentation in this repository.

## Building the Project

MWPV currently targets:

- Windows
- .NET 8
- WPF
- Visual Studio or another compatible .NET development environment

Additional dependencies and build instructions will be documented before release.

## Data and Privacy

This repository must not contain:

- Real password-vault databases
- Key files
- User passwords
- Encryption keys
- Personal logs
- Production configuration secrets
- Signing credentials
- Private user data

Any sample data included in the future should be fictional and clearly identified as test data.

## Contributions

This repository is publicly visible for review and documentation access.

Direct write access is restricted to the repository owner. External users may review or fork the repository, but changes are not accepted into this repository unless explicitly reviewed and approved by the owner.

## License

MWPV is distributed under the **MWPV Free Use and Source Review License**.

See the root `LICENSE` file for the complete terms.

The presence of publicly available source code does not automatically place the project in the public domain or grant unrestricted commercial rights.

## Disclaimer

MWPV is under active development and may contain defects, incomplete features, or undocumented behavior.

Do not use development builds to store information that you cannot afford to lose. Maintain independent backups of important data.

## Author

Developed by Dan Miller.

Project website:

`https://dansgeekstop.com`
