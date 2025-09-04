# MWPV – 2025-08-16 Code/XAML Pre‑Review (Automated Scan)

## 1) Early login .elog path

- [ ] EarlyLoginFailures.cs present: MISSING
- [ ] References to '.elog' / early folder: NO
- [ ] DPAPI usage detected (ProtectedData/DataProtectionScope)

## 2) LogRepository (K1 format)

- [ ] LogRepository.cs present: MISSING
- [ ] Uses LogPayloadKey for writes
- [ ] Wraps payload with { data, meta { keySetVersion, payloadVer } }
- [ ] PayloadFmt uses 'json+aesgcm'

## 3) EarlyLogEntryV1 & SQL loading consistency

- [ ] EarlyLogEntryV1.cs present: MISSING
- [ ] Uses Utilities.Sql.SecureSql.Require(...)
- [x] No hardcoded SQL detected in EarlyLogEntryV1

## 5) MainWindow split layout

- [ ] MainWindow.xaml present: MISSING
- [ ] GridSplitter present
- [ ] Two-column <ColumnDefinitions> detected
- [ ] Category List identifiers present in XAML
- [ ] Category Items identifiers present in XAML

## SQL access patterns

- [ ] Uses SecureEncryptedDataStore/SecureSql lookups somewhere in code
- [x] No hardcoded SQL found across codebase (sampled checks)