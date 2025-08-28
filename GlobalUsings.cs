// Make DLL namespaces available app-wide
global using Security.Utility;
global using Security.Utility.Storage;
global using Security.Utility.Wiping;
global using Security.Utility.Logging;

// Aliases for convenience / backward-compat
global using SecureEncryptedDataStore = Security.Utility.Storage.SecureEncryptedDataStore;
global using SensitiveDataCleaner = Security.Utility.Wiping.SensitiveDataCleaner;
global using SecLogLevel = Security.Utility.Logging.LogSeverity;  // <-- this defines SecLogLevel
