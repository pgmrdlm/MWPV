// File: MWPV/Utilities/Helpers/ClipboardHelper.cs (FULL REWRITE)
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace MWPV.Utilities.Helpers
{
    /// <summary>
    /// Central clipboard helper with best-effort TTL clearing for sensitive text.
    ///
    /// Notes:
    /// - Clipboard is inherently plaintext once copied.
    /// - TTL clear is best-effort (user/apps may overwrite clipboard).
    /// - We DO NOT store plaintext in static fields; we store only hash+length to detect "unchanged".
    /// </summary>
    public static class ClipboardHelper
    {
        private static readonly object _sync = new();

        private static AutoHideTimer? _timer;
        private static TimeSpan _ttl = TimeSpan.FromSeconds(45);

        // Fingerprint of last copied value (hash+length). No plaintext stored.
        private static byte[]? _lastHash32;
        private static int _lastLen;

        public static TimeSpan DefaultTtl
        {
            get { lock (_sync) return _ttl; }
            set
            {
                lock (_sync)
                {
                    _ttl = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(45) : value;
                    if (_timer != null) _timer.Interval = _ttl;
                }
            }
        }

        /// <summary>
        /// Copy text to clipboard and start/reset TTL clearing.
        /// Returns false if text is null/empty or clipboard operation fails.
        /// </summary>
        public static bool TryCopySensitiveText(string? text, out string reason, TimeSpan? ttlOverride = null, string? tag = null)
        {
            reason = string.Empty;

            if (string.IsNullOrEmpty(text))
            {
                reason = "Empty";
                return false;
            }

            try
            {
                // Compute fingerprint first (no plaintext retention beyond this call).
                var fp = Fingerprint(text);

                // Clipboard should be accessed on the UI thread; in WPF we’re typically already there.
                // If not, this still works because Clipboard uses STA; failures are caught.
                Clipboard.SetText(text);

                lock (_sync)
                {
                    _lastHash32 = fp.hash32;
                    _lastLen = fp.len;

                    var ttl = ttlOverride ?? _ttl;
                    if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromSeconds(45);

                    EnsureTimer_NoLock(ttl);
                    _timer!.Touch(true);
                }

#if DEBUG
                Debug.WriteLine($"[CLIPBOARD][{tag ?? "Copy"}] Copied len={text.Length}, ttl={(ttlOverride ?? DefaultTtl).TotalSeconds:0}s");
#endif
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
#if DEBUG
                Debug.WriteLine($"[CLIPBOARD][{tag ?? "Copy"}] Copy failed: {ex.GetType().Name}");
#endif
                return false;
            }
        }

        /// <summary>
        /// Manually stop the TTL timer (does not clear clipboard).
        /// </summary>
        public static void StopTimer()
        {
            lock (_sync)
            {
                _timer?.Stop();
            }
        }

        /// <summary>
        /// Best-effort: clear clipboard only if it still matches what we last copied.
        /// Safe to call from anywhere; failures are ignored.
        /// </summary>
        public static void ClearIfStillMatchesLast(string? tag = null)
        {
            try
            {
                string current = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                if (string.IsNullOrEmpty(current)) return;

                byte[] currentHash;
                int currentLen;
                (currentHash, currentLen) = Fingerprint(current);

                bool shouldClear = false;

                lock (_sync)
                {
                    if (_lastHash32 != null &&
                        _lastLen == currentLen &&
                        FixedTimeEquals(_lastHash32, currentHash))
                    {
                        shouldClear = true;
                    }
                }

                if (shouldClear)
                {
                    Clipboard.Clear();
#if DEBUG
                    Debug.WriteLine($"[CLIPBOARD][{tag ?? "Clear"}] Cleared (matched last) len={currentLen}");
#endif
                }

                // Best-effort wipe temp hash buffer
                Array.Clear(currentHash, 0, currentHash.Length);
            }
            catch
            {
                // Best-effort. Ignore.
            }
        }

        private static void EnsureTimer_NoLock(TimeSpan interval)
        {
            if (_timer == null)
            {
                _timer = new AutoHideTimer(interval, () =>
                {
                    // Timer callback (UI dispatcher). Clear if still matches last.
                    ClearIfStillMatchesLast("TTL");
                });

                _ttl = interval;
            }
            else
            {
                _timer.Interval = interval;
                _ttl = interval;
            }
        }

        private static (byte[] hash32, int len) Fingerprint(string s)
        {
            // Hash UTF-8 bytes. We wipe the temporary byte[] buffer after hashing.
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            try
            {
                byte[] hash = SHA256.HashData(bytes);
                return (hash, s.Length);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
