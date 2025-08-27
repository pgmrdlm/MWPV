// Makes the DLL types visible everywhere in the app:
global using Security.Utility;

// (optional shims so old code that types the bare names still compiles)
global using SecureEncryptedDataStore = Security.Utility.SecureEncryptedDataStore;
global using SensitiveDataCleaner = Security.Utility.SensitiveDataCleaner;
global using EarlyFailType = Utilities.Diagnostics.EarlyFailType;