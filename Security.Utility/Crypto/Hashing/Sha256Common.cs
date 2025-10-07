// File: Security/Hash/Sha256Common.cs
using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Security.Utility.Crypto.Hash
{
    /// <summary>
    /// Central SHA-256 helper covering strings, spans, byte[], streams, and files.
    /// Returns lowercase hex; includes short-hash helpers for identifiers.
    /// </summary>
    public static class Sha256Common
    {
        // =========================
        // Core HEX helpers
        // =========================

        /// <summary>SHA-256 of UTF-8 string -> lowercase hex.</summary>
        public static string Hex(string input, Encoding? enc = null)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            enc ??= Encoding.UTF8;
            return Hex(enc.GetBytes(input));
        }

        /// <summary>SHA-256 of byte[] -> lowercase hex.</summary>
        public static string Hex(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
#if NET8_0_OR_GREATER
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(data, hash);
            return ToHex(hash);
#else
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(data));
#endif
        }

        /// <summary>SHA-256 of ReadOnlySpan&lt;byte&gt; -> lowercase hex (allocation-free).</summary>
        public static string Hex(ReadOnlySpan<byte> data)
        {
#if NET8_0_OR_GREATER
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(data, hash);
            return ToHex(hash);
#else
            // Fallback copies into array for older TFMs.
            var arr = data.ToArray();
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(arr));
#endif
        }

        /// <summary>SHA-256 of a Stream -> lowercase hex. Stream is read from current Position to end.</summary>
        public static string Hex(Stream stream, bool leaveOpen = false)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
#if NET8_0_OR_GREATER
            using var sha = SHA256.Create(); // no static stream overload returns Span in .NET 8
            var hash = sha.ComputeHash(stream);
            if (!leaveOpen) stream.Dispose();
            return ToHex(hash);
#else
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            if (!leaveOpen) stream.Dispose();
            return ToHex(hash);
#endif
        }

        /// <summary>SHA-256 of a file path -> lowercase hex.</summary>
        public static string HexFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Hex(fs, leaveOpen: false);
        }

        // =========================
        // Short-hash helpers (first N bytes -> 2N hex chars)
        // =========================

        /// <summary>
        /// Short SHA-256 hex of a UTF-8 string (first <paramref name="takeBytes"/> of the 32-byte digest).
        /// Default 6 bytes => 12 hex chars.
        /// </summary>
        public static string ShortHex(string input, int takeBytes = 6, Encoding? enc = null)
        {
            if (string.IsNullOrEmpty(input)) return "0";
            enc ??= Encoding.UTF8;
            return ShortHex(enc.GetBytes(input), takeBytes);
        }

        /// <summary>Short SHA-256 hex of byte[] (first <paramref name="takeBytes"/> bytes).</summary>
        public static string ShortHex(byte[] data, int takeBytes = 6)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
#if NET8_0_OR_GREATER
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(data, hash);
            return ToHex(hash[..ClampTake(takeBytes)]);
#else
            using var sha = SHA256.Create();
            var full = sha.ComputeHash(data);
            return ToHex(full, takeBytes);
#endif
        }

        /// <summary>Short SHA-256 hex of span (first <paramref name="takeBytes"/> bytes).</summary>
        public static string ShortHex(ReadOnlySpan<byte> data, int takeBytes = 6)
        {
#if NET8_0_OR_GREATER
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(data, hash);
            return ToHex(hash[..ClampTake(takeBytes)]);
#else
            var arr = data.ToArray();
            using var sha = SHA256.Create();
            var full = sha.ComputeHash(arr);
            return ToHex(full, takeBytes);
#endif
        }

        /// <summary>Short SHA-256 hex of a file (first <paramref name="takeBytes"/> bytes of digest).</summary>
        public static string ShortHexFile(string path, int takeBytes = 6) =>
            ToHex(GetFileHash(path)[..ClampTake(takeBytes)]);

        // =========================
        // Try- helpers (don’t throw)
        // =========================

        public static bool TryHexFile(string path, out string? hex)
        {
            try { hex = HexFile(path); return true; }
            catch { hex = null; return false; }
        }

        public static bool TryShortHexFile(string path, out string? shortHex, int takeBytes = 6)
        {
            try { shortHex = ShortHexFile(path, takeBytes); return true; }
            catch { shortHex = null; return false; }
        }

        // =========================
        // Internals
        // =========================

        private static byte[] GetFileHash(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            return sha.ComputeHash(fs);
        }

        private static int ClampTake(int takeBytes) =>
            takeBytes <= 0 ? 1 : (takeBytes > 32 ? 32 : takeBytes);

        private static string ToHex(ReadOnlySpan<byte> bytes)
        {
            // Fast hex encode without per-byte string allocations.
            char[] chars = ArrayPool<char>.Shared.Rent(bytes.Length * 2);
            try
            {
                int ci = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte b = bytes[i];
                    chars[ci++] = GetHexNibble(b >> 4);
                    chars[ci++] = GetHexNibble(b & 0xF);
                }
                return new string(chars, 0, ci);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }
        }

        private static string ToHex(byte[] full, int takeBytes)
        {
            if (full is null || full.Length == 0) return string.Empty;
            takeBytes = ClampTake(takeBytes);
            return ToHex(full.AsSpan(0, Math.Min(takeBytes, full.Length)));
        }

        private static char GetHexNibble(int value) =>
            (char)(value < 10 ? ('0' + value) : ('a' + (value - 10)));
    }
}
