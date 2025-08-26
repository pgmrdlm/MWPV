# MWPV — Security & Logging

## 1) Class Diagram

```mermaid
classDiagram
  class AppEntryWindow {
    +Submit()
    +btnCreateKeyFile_Click()
  }

  class ServiceSetUp {
    +SetUpDataBase() string
    +SetUpKeyFile() string
    +EnsureKeySetFromArchive() void
  }

  class SecureEncryptedDataStore {
    +SetAndWipe(key, value)
    +SetNoWipe(key, value)
    +SetString(key, value)
    +GetBytes(key)
    +GetChars(key)
  }

  class SensitiveDataCleaner {
    +ClearPasswordBox()
    +ClearTextBox()
    +ClearChars()
  }

  class KeyArchiveVerifier {
    +VerifyPasswordAndSentinels(path, password) bool
  }

  class SqlCatagory {
    +EnsureKeysAndLoadAll()
    +GetSql(name) string
    +Require(name) string
    +GetMissingMustHaves() string[]
  }

  class SchemaBootstrap {
    +EnsureLogsSchema(conn)
  }

  class DatabaseHelper {
    +OpenConnection() SqliteConnection
  }

  class SecureLogService {
    +Initialize()
    +LoadInsertSql()
  }

  class LogRepository {
    +InsertAsync(entry) Task<long>
    +GetRecentAsync(limit, fromUtc) Task<IEnumerable>
    +GetLastInsertIdAsync() Task<long>
  }

  class EarlyLoginFailures {
    +HasPending() bool
    +Record(type, message) void
    +RecordEx(type, message, ex) void
    +IngestAll()
  }

  class ErrorHandler {
    +InfoTitled(title, message, context)
    +WarnTitled(title, message, context)
    +AskYesNo(title, message, context) MessageBoxResult
  }

  AppEntryWindow --> ServiceSetUp
  AppEntryWindow --> SecureEncryptedDataStore
  AppEntryWindow --> SensitiveDataCleaner
  AppEntryWindow --> KeyArchiveVerifier
  AppEntryWindow --> SqlCatagory
  AppEntryWindow --> SchemaBootstrap
  AppEntryWindow --> ErrorHandler

  ServiceSetUp --> SecureEncryptedDataStore
  SecureLogService --> LogRepository
  LogRepository --> DatabaseHelper
  SchemaBootstrap --> DatabaseHelper
  EarlyLoginFailures --> LogRepository
```

---

## 2) Startup / Security Flow

```mermaid
flowchart TD
    A[App Start\n(App.xaml.cs)] --> B[AppEntryWindow]
    B -->|First Run| C[ServiceSetUp\nCreate DB + Key Archive]
    B -->|Existing Install| D[KeyArchiveVerifier\nVerify password + sentinels]

    C --> E[SecureEncryptedDataStore\nStore DB_Password + KeyPW]
    D --> E

    E --> F[SqlCatagory\nEnsureKeysAndLoadAll()]
    F --> G[SchemaBootstrap\nEnsure Logs schema]
    G --> H[DatabaseHelper\nOpenConnection()]

    H --> I[SecureLogService\nInitialize + load INSERT SQL]
    I --> J[LogRepository\nInsert / Select / LastId ready]

    %% Early logs and cleanup
    B --> K[EarlyLoginFailures\n(.elog on errors)]
    K -->|On next good login| J
```
