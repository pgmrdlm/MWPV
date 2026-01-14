using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        Debug.WriteLine("[STORE] Initialized (session AES key generated, wiper registered).");
    }

#if DEBUG
    private static void DebugCaller(string op, string? detail = null)
    {
        try
        {
            // 0 = this method
            // 1 = the immediate caller (Clear/ClearAll/WipeAll)
            // 2+ = the real external caller we care about
            var st = new StackTrace(skipFrames: 2, fNeedFileInfo: true);
            Debug.WriteLine($"[STORE][CALLER] {op}" + (string.IsNullOrWhiteSpace(detail) ? "" : $" ({detail})"));
            Debug.WriteLine(st.ToString());
        }
        catch
        {
            // Never let diagnostics interfere with runtime.
        }
    }
#endif

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
            //Debug.WriteLine($"[STORE] Set entry (total={_store.Count}).");
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

            //Debug.WriteLine($"[STORE] Get bytes (entries={_store.Count}).");

            // Decrypt under the same lock so Key cannot be wiped mid-decrypt.
            // NOTE: DecryptWithEmbeddedNonce_NoLock allocates new plaintext buffer.
            return DecryptWithEmbeddedNonce_NoLock(key, combinedCopy);
        }
        // (combinedCopy is only ciphertext; no need to wipe here, but it will be GC'd)
    }

    /// <summary>Try get plaintext bytes. Returns false if missing (no exception).</summary>
    public static bool TryGetBytes(string key, out byte[] plainBytes)
    {
        plainBytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(key))
            return false;

        lock (_gate)
        {
            if (_wiped) return false;
            if (!_store.TryGetValue(key, out var combined))
                return false;

            var combinedCopy = new byte[combined.Length];
            Buffer.BlockCopy(combined, 0, combinedCopy, 0, combined.Length);

            try
            {
                plainBytes = DecryptWithEmbeddedNonce_NoLock(key, combinedCopy);
                return true;
            }
            catch
            {
                // Treat tamper/wrong-key as failure for "Try" API.
                return false;
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
    {
        value = 0;
        if (!TryGetBytes(key, out var bytes) || bytes.Length != 4)
        {
            if (bytes.Length != 0) Array.Clear(bytes, 0, bytes.Length);
            return false;
        }

        try
        {
            value =
                bytes[0]
                | (bytes[1] << 8)
                | (bytes[2] << 16)
                | (bytes[3] << 24);

            return true;
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
            bool exists = _store.ContainsKey(key);
            //Debug.WriteLine($"[STORE] HasKey={exists} (entries={_store.Count}).");
            return exists;
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

#if DEBUG
        DebugCaller("Clear(key)", key);
#endif

        lock (_gate)
        {
            ThrowIfWiped();
            if (_store.TryGetValue(key, out var combined))
            {
                Array.Clear(combined, 0, combined.Length);
                _store.Remove(key);
                Debug.WriteLine($"[STORE] Cleared one entry (entries={_store.Count}).");
            }
        }
    }

    public static void ClearAll()
    {
#if DEBUG
        DebugCaller("ClearAll()");
#endif

        lock (_gate)
        {
            ThrowIfWiped();
            foreach (var kvp in _store)
            {
                Array.Clear(kvp.Value, 0, kvp.Value.Length);
            }
            _store.Clear();
            Debug.WriteLine("[STORE] Cleared all entries (entries=0).");
        }
    }

    public static IEnumerable<string> Keys()
    {
        lock (_gate)
        {
            ThrowIfWiped();
           // Debug.WriteLine($"[STORE] Keys requested (entries={_store.Count}).");
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
#if DEBUG
        DebugCaller("WipeAll()");
#endif

        lock (_gate)
        {
            if (_wiped)
            {
                Debug.WriteLine("[WIPE] Store.WipeAll skipped (already wiped).");
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
            Debug.WriteLine("[WIPE] Store.WipeAll executed (entries=0, key zeroed).");
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

    // =========================
    //    OPTIONAL DIAGNOSTICS
    // =========================

    public static void DebugDumpKeys()
    {
        lock (_gate)
        {
            Debug.WriteLine($"[DEBUG] Store dump: wiped={_wiped}, entries={_store.Count}");
        }
    }

    public static void DebugStatus(string? note = null)
    {
        lock (_gate)
        {
            Debug.WriteLine($"[DEBUG] Store status: wiped={_wiped}, entries={_store.Count}" +
                            (string.IsNullOrEmpty(note) ? "" : $" note={note}"));
        }
    }
}
