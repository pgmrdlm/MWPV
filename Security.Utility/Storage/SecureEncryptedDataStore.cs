using System;
using System.Collections.Generic;
using System.Diagnostics; // Debug.WriteLine traces (always-on)
using System.Security.Cryptography;
using System.Text;
using Security.Utility.Wiping;

namespace Security.Utility.Storage;

/// <summary>
/// In-memory encrypted key/value store for sensitive data.
/// - Stores ciphertext only (NONCE(12) || TAG(16) || CIPHERTEXT per entry, AES-GCM).
/// - Uses the logical key name as AAD to bind entries and prevent swap attacks.
/// - Returns plaintext as byte[] or char[] (caller must wipe).
/// - Session AES-256 key lives in-process and is wiped on global shutdown.
/// </summary>
public static class SecureEncryptedDataStore
{
    private const int NonceSize = 12; // recommended for AesGcm
    private const int TagSize = 16; // 128-bit tag

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

    private static void SetBytesInternal(string key, byte[] plainBytes)
    {
        // Encrypt with a fresh random NONCE per entry + AAD=key name
        byte[] combined = EncryptWithRandomNonce(key, plainBytes);

        // Defensive copy into store
        var copy = new byte[combined.Length];
        Buffer.BlockCopy(combined, 0, copy, 0, combined.Length);

        lock (_gate)
        {
            ThrowIfWiped();
            if (_store.TryGetValue(key, out var existing))
            {
                Array.Clear(existing, 0, existing.Length);
            }
            _store[key] = copy;
            Debug.WriteLine($"[STORE] Set entry (total={_store.Count}).");
        }

        // Wipe our temporary combined buffer
        Array.Clear(combined, 0, combined.Length);
    }

    // =========================
    //      NON-SENSITIVE HELPERS
    // =========================

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

    // =========================
    //            GET
    // =========================

    /// <summary>Get plaintext bytes. Caller MUST wipe the returned buffer after use.</summary>
    public static byte[] GetBytes(string key)
    {
        byte[] combined;
        lock (_gate)
        {
            ThrowIfWiped();
            if (!_store.TryGetValue(key, out combined!))
                throw new KeyNotFoundException("Key not found.");
            Debug.WriteLine($"[STORE] Get bytes (entries={_store.Count}).");
        }

        // Decrypt (creates a new plaintext buffer)
        return DecryptWithEmbeddedNonce(key, combined);
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

    // =========================
    //       HOUSEKEEPING
    // =========================

    public static bool HasKey(string key)
    {
        lock (_gate)
        {
            ThrowIfWiped();
            bool exists = _store.ContainsKey(key);
            Debug.WriteLine($"[STORE] HasKey={exists} (entries={_store.Count}).");
            return exists;
        }
    }

    public static void Clear(string key)
    {
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
            // Don’t log names; just the count.
            Debug.WriteLine($"[STORE] Keys requested (entries={_store.Count}).");
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
                Debug.WriteLine("[WIPE] Store.WipeAll skipped (already wiped).");
                return;
            }

            // wipe entries
            foreach (var kvp in _store)
            {
                var buf = kvp.Value;
                if (buf is { Length: > 0 })
                    Array.Clear(buf, 0, buf.Length);
            }
            _store.Clear();

            // wipe session key
            if (Key is { Length: > 0 })
                Array.Clear(Key, 0, Key.Length);

            _wiped = true;
            Debug.WriteLine("[WIPE] Store.WipeAll executed (entries=0, key zeroed).");
        }
    }

    private static void ThrowIfWiped()
    {
        if (_wiped) throw new ObjectDisposedException(nameof(SecureEncryptedDataStore), "Store is wiped/closed.");
    }

    // =========================
    //     CRYPTO PRIMITIVES
    // =========================
    // Layout: NONCE(12) || TAG(16) || CIPHERTEXT

    private static byte[] EncryptWithRandomNonce(string keyName, byte[] plain)
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

        // layout: NONCE || TAG || CIPHERTEXT
        var combined = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceSize + TagSize, ciphertext.Length);

        // wipe temps
        Array.Clear(nonce, 0, nonce.Length);
        Array.Clear(tag, 0, tag.Length);
        Array.Clear(ciphertext, 0, ciphertext.Length);
        Array.Clear(aad, 0, aad.Length);

        return combined;
    }

    private static byte[] DecryptWithEmbeddedNonce(string keyName, byte[] combined)
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
            // Throws CryptographicException if tampered/wrong key/AAD
            gcm.Decrypt(nonce, ciphertext, tag, plain, aad);
        }

        // wipe temps
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
