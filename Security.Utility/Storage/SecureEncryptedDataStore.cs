using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Security.Utility.Wiping;

namespace Security.Utility.Storage;

/// <summary>
/// In-memory encrypted key/value store for sensitive data.
/// - Stores ciphertext only (NONCE(12) || TAG(16) || CIPHERTEXT per entry, AES-GCM).
/// - Uses the logical key name as AAD to bind entries and prevent swap attacks.
/// - Returns plaintext as byte[] or char[] (caller must wipe returned buffers).
/// - Session AES-256 key lives in-process and is wiped on global shutdown.
/// </summary>
public static class SecureEncryptedDataStore
{
    private const int NonceSize = 12; // recommended for AesGcm
    private const int TagSize = 16;   // 128-bit tag

    // Reserved "context" keys (generic; still app-agnostic).
    // If we want a single "current entity id" slot, we MUST also store what it refers to.
    public static class ContextKeys
    {
        /// <summary>String describing what entity the CurrentEntityId refers to (ex: "CategoryItem").</summary>
        public const string CurrentEntityKind = "__CTX__CurrentEntityKind";

        /// <summary>
        /// Current entity primary key (int, stored as 4 bytes little-endian).
        /// Convention: 0 => Add/new; >0 => Edit existing.
        /// </summary>
        public const string CurrentEntityId = "__CTX__CurrentEntityId";
    }

    // Session-scoped AES-256 key (mutable for zeroization)
    private static readonly byte[] Key;

    // In-memory ciphertext: value = NONCE||TAG||CIPHERTEXT
    private static readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

    // Concurrency gate + wipe guard
    private static readonly object _gate = new();
    private static bool _wiped;

    // Register a wiper with the global cleaner so App.SafeWipeAll() reaches us.
    private sealed class StoreWiper : ISensitiveWipe
    {
        public void Wipe() => SecureEncryptedDataStore.WipeAll();
    }

    static SecureEncryptedDataStore()
    {
        Key = new byte[32]; // AES-256
        RandomNumberGenerator.Fill(Key);

        // Register once with the global cleaner
        SensitiveDataCleaner.Register(new StoreWiper());

    }

    // =========================
    //            SET
    // =========================

    /// <summary>Store from char[] and WIPE the caller's buffer afterward (safest default).</summary>
    public static void SetAndWipe(string key, char[] plainChars)
    {
        if (plainChars is null) throw new ArgumentNullException(nameof(plainChars));

        byte[]? utf8 = null;
        try
        {
            utf8 = Encoding.UTF8.GetBytes(plainChars);
            SetBytesInternal(key, utf8);
        }
        finally
        {
            SensitiveDataCleaner.WipeCharArray(plainChars);
            if (utf8 is not null) Array.Clear(utf8, 0, utf8.Length);
        }
    }

    /// <summary>Store from char[] WITHOUT wiping the caller's buffer.</summary>
    public static void SetNoWipe(string key, char[] plainChars)
    {
        if (plainChars is null) throw new ArgumentNullException(nameof(plainChars));

        byte[]? utf8 = null;
        try
        {
            utf8 = Encoding.UTF8.GetBytes(plainChars);
            SetBytesInternal(key, utf8);
        }
        finally
        {
            if (utf8 is not null) Array.Clear(utf8, 0, utf8.Length);
        }
    }

    /// <summary>Store from byte[]. Caller retains control of wiping their input buffer.</summary>
    public static void Set(string key, byte[] plainBytes)
    {
        if (plainBytes is null) throw new ArgumentNullException(nameof(plainBytes));
        SetBytesInternal(key, plainBytes);
    }

    /// <summary>
    /// Store from byte[] and WIPE the caller's buffer afterward (no 'ref' required).
    /// Zeros the caller's array IN PLACE but keeps the same array instance.
    /// </summary>
    public static void SetAndWipe(string key, byte[] plainBytes)
    {
        if (plainBytes is null) throw new ArgumentNullException(nameof(plainBytes));
        try
        {
            SetBytesInternal(key, plainBytes);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    /// <summary>
    /// Store a NON-SENSITIVE string (e.g., file paths, SQL). Still encrypted at rest in memory;
    /// the original string cannot be zeroized.
    /// </summary>
    public static void SetString(string key, string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            Set(key, bytes);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length); // string itself can't be wiped
        }
    }

    /// <summary>Store a 32-bit integer (little-endian).</summary>
    public static void SetInt32(string key, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        unchecked
        {
            buf[0] = (byte)(value);
            buf[1] = (byte)(value >> 8);
            buf[2] = (byte)(value >> 16);
            buf[3] = (byte)(value >> 24);
        }

        // stackalloc is already ephemeral; still copy to heap array to pass to SetBytesInternal
        var tmp = buf.ToArray();
        try
        {
            SetBytesInternal(key, tmp);
        }
        finally
        {
            Array.Clear(tmp, 0, tmp.Length);
        }
    }

    private static void SetBytesInternal(string key, byte[] plainBytes)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key name is required.", nameof(key));

        byte[] combined;
        lock (_gate)
        {
            ThrowIfWiped();

            // Encrypt under lock so WipeAll() cannot zero Key mid-operation.
            combined = EncryptWithRandomNonce_NoLock(key, plainBytes);

            // Defensive copy into store
            var copy = new byte[combined.Length];
            Buffer.BlockCopy(combined, 0, copy, 0, combined.Length);

            if (_store.TryGetValue(key, out var existing))
            {
                Array.Clear(existing, 0, existing.Length);
            }

            _store[key] = copy;
        }

        // Wipe our temporary combined buffer
        Array.Clear(combined, 0, combined.Length);
    }

    // =========================
    //            GET
    // =========================

    /// <summary>Get plaintext bytes. Caller MUST wipe the returned buffer after use.</summary>
    public static byte[] GetBytes(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key name is required.", nameof(key));

        byte[] combinedCopy;

        lock (_gate)
        {
            ThrowIfWiped();

            if (!_store.TryGetValue(key, out var combined))
                throw new KeyNotFoundException("Key not found.");

            // IMPORTANT: copy ciphertext while under lock so Clear/WipeAll can't zero it mid-decrypt.
            combinedCopy = new byte[combined.Length];
            Buffer.BlockCopy(combined, 0, combinedCopy, 0, combined.Length);

            // Decrypt under the same lock so Key cannot be wiped mid-decrypt.
            // NOTE: DecryptWithEmbeddedNonce_NoLock allocates new plaintext buffer.
            return DecryptWithEmbeddedNonce_NoLock(key, combinedCopy);
        }
        // (combinedCopy is only ciphertext; no need to wipe here, but it will be GC'd)
    }

    /// <summary>Try get plaintext bytes. Returns false if missing (no exception).</summary>
    public static bool TryGetBytes(string key, out byte[] plainBytes)
        => TryGetBytesResult(key, out plainBytes).Succeeded;

    /// <summary>
    /// Try get plaintext bytes and return a Security.Utility technical result.
    /// The result does not include message text, exception text, plaintext,
    /// ciphertext, keys, passwords, sensitive paths, or caller actions.
    /// </summary>
    public static SecurityUtilityResult TryGetBytesResult(string key, out byte[] plainBytes)
    {
        plainBytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(key))
            return Result(SecurityUtilityReturnCode.InvalidInput, SecurityUtilityResultKind.Failure);

        lock (_gate)
        {
            if (_wiped)
                return Result(SecurityUtilityReturnCode.SecureStoreUnavailable, SecurityUtilityResultKind.Abend);

            if (!_store.TryGetValue(key, out var combined))
                return Result(SecurityUtilityReturnCode.SecureStoreKeyMissing, SecurityUtilityResultKind.Failure);

            var combinedCopy = new byte[combined.Length];
            Buffer.BlockCopy(combined, 0, combinedCopy, 0, combined.Length);

            try
            {
                plainBytes = DecryptWithEmbeddedNonce_NoLock(key, combinedCopy);
                return Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success);
            }
            catch (CryptographicException)
            {
                return Result(SecurityUtilityReturnCode.ProtectedDataDecryptFailed, SecurityUtilityResultKind.Abend);
            }
            catch
            {
                return Result(SecurityUtilityReturnCode.UnknownSecurityFailure, SecurityUtilityResultKind.Abend);
            }
        }
    }

    /// <summary>Get plaintext chars (UTF-8). Caller MUST wipe the returned buffer after use.</summary>
    public static char[] GetChars(string key)
    {
        byte[] bytes = GetBytes(key);
        try
        {
            var decoder = Encoding.UTF8.GetDecoder();
            int charCount = decoder.GetCharCount(bytes, 0, bytes.Length);
            var chars = new char[charCount];
            decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: true);
            return chars;
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    /// <summary>Retrieve a NON-SENSITIVE string.</summary>
    public static string GetString(string key)
    {
        byte[] bytes = GetBytes(key);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    /// <summary>Get a 32-bit integer (little-endian). Throws if missing.</summary>
    public static int GetInt32(string key)
    {
        byte[] bytes = GetBytes(key);
        try
        {
            if (bytes.Length != 4)
                throw new CryptographicException("Stored Int32 value has invalid length.");

            int value =
                bytes[0]
                | (bytes[1] << 8)
                | (bytes[2] << 16)
                | (bytes[3] << 24);

            return value;
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    /// <summary>Try get a 32-bit integer (little-endian). Returns false if missing or invalid.</summary>
    public static bool TryGetInt32(string key, out int value)
        => TryGetInt32Result(key, out value).Succeeded;

    /// <summary>
    /// Try get a 32-bit integer and return a Security.Utility technical result.
    /// The result reports only code and seriousness; callers decide behavior.
    /// </summary>
    public static SecurityUtilityResult TryGetInt32Result(string key, out int value)
    {
        value = 0;

        var result = TryGetBytesResult(key, out var bytes);
        if (!result.Succeeded)
            return result;

        if (bytes.Length != 4)
        {
            if (bytes.Length != 0) Array.Clear(bytes, 0, bytes.Length);
            return Result(SecurityUtilityReturnCode.ProtectedDataMalformed, SecurityUtilityResultKind.Abend);
        }

        try
        {
            value =
                bytes[0]
                | (bytes[1] << 8)
                | (bytes[2] << 16)
                | (bytes[3] << 24);

            return Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    // =========================
    //       HOUSEKEEPING
    // =========================

    public static bool HasKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        lock (_gate)
        {
            ThrowIfWiped();
            return _store.ContainsKey(key);
        }
    }

    /// <summary>
    /// Clear ONLY the generic context keys (CurrentEntityKind/CurrentEntityId).
    /// This is the safe "navigation clear" for UI panels.
    /// </summary>
    public static void ClearContext()
    {
        Clear(ContextKeys.CurrentEntityKind);
        Clear(ContextKeys.CurrentEntityId);
    }

    public static void Clear(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        lock (_gate)
        {
            ThrowIfWiped();
            if (_store.TryGetValue(key, out var combined))
            {
                Array.Clear(combined, 0, combined.Length);
                _store.Remove(key);
            }
        }
    }

    public static void ClearAll()
    {
        lock (_gate)
        {
            ThrowIfWiped();
            foreach (var kvp in _store)
            {
                Array.Clear(kvp.Value, 0, kvp.Value.Length);
            }
            _store.Clear();
        }
    }

    public static IEnumerable<string> Keys()
    {
        lock (_gate)
        {
            ThrowIfWiped();
            return new List<string>(_store.Keys);
        }
    }

    /// <summary>
    /// Global wipe for this store. Zeroizes the AES key and all ciphertext buffers.
    /// Called via SensitiveDataCleaner.WipeAll() on shutdown/abends.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    public static void WipeAll()
    {
        lock (_gate)
        {
            if (_wiped)
            {
                return;
            }

            foreach (var kvp in _store)
            {
                var buf = kvp.Value;
                if (buf is { Length: > 0 })
                    Array.Clear(buf, 0, buf.Length);
            }
            _store.Clear();

            if (Key is { Length: > 0 })
                Array.Clear(Key, 0, Key.Length);

            _wiped = true;
        }
    }

    private static void ThrowIfWiped()
    {
        if (_wiped)
            throw new ObjectDisposedException(nameof(SecureEncryptedDataStore), "Store is wiped/closed.");
    }

    // =========================
    //     CRYPTO PRIMITIVES
    // =========================
    // Layout: NONCE(12) || TAG(16) || CIPHERTEXT
    // NOTE: These are "_NoLock" and must be called WITH _gate held.

    private static byte[] EncryptWithRandomNonce_NoLock(string keyName, byte[] plain)
    {
        // AAD binds this ciphertext to the logical key 'keyName'
        byte[] aad = Encoding.UTF8.GetBytes(keyName);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var gcm = new AesGcm(Key))
        {
            gcm.Encrypt(nonce, plain, ciphertext, tag, aad);
        }

        var combined = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceSize + TagSize, ciphertext.Length);

        Array.Clear(nonce, 0, nonce.Length);
        Array.Clear(tag, 0, tag.Length);
        Array.Clear(ciphertext, 0, ciphertext.Length);
        Array.Clear(aad, 0, aad.Length);

        return combined;
    }

    private static byte[] DecryptWithEmbeddedNonce_NoLock(string keyName, byte[] combined)
    {
        if (combined is null || combined.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext is too short.");

        byte[] aad = Encoding.UTF8.GetBytes(keyName);

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];

        Buffer.BlockCopy(combined, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(combined, NonceSize, tag, 0, TagSize);

        int ctLen = combined.Length - NonceSize - TagSize;
        var ciphertext = new byte[ctLen];
        Buffer.BlockCopy(combined, NonceSize + TagSize, ciphertext, 0, ctLen);

        var plain = new byte[ctLen];
        using (var gcm = new AesGcm(Key))
        {
            gcm.Decrypt(nonce, ciphertext, tag, plain, aad);
        }

        Array.Clear(nonce, 0, nonce.Length);
        Array.Clear(tag, 0, tag.Length);
        Array.Clear(ciphertext, 0, ciphertext.Length);
        Array.Clear(aad, 0, aad.Length);

        return plain;
    }

    private static SecurityUtilityResult Result(
        SecurityUtilityReturnCode code,
        SecurityUtilityResultKind kind)
        => new()
        {
            Code = code,
            Kind = kind
        };

}
