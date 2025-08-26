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