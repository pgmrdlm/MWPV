using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using MWPV.Services;
using MWPV.Utilities.Helpers;

namespace MWPV.Services.Security
{
    public sealed class SensitiveClipboardService : IClipboardService
    {
        public static SensitiveClipboardService Shared { get; } = new();

        private const int ClipboardRetryCount = 2;
        private static readonly TimeSpan ClipboardRetryDelay = TimeSpan.FromMilliseconds(35);

        private readonly object _sync = new();
        private AutoHideTimer? _timer;
        private byte[]? _ownedHash32;
        private int _ownedLength;

        private SensitiveClipboardService()
        {
        }

        public bool CopySensitiveText(string value, string reasonCode)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            reasonCode = NormalizeReason(reasonCode, "SensitiveClipboardCopied");

            byte[]? newHash = null;
            try
            {
                var fingerprint = Fingerprint(value);
                newHash = fingerprint.Hash32;

                lock (_sync)
                {
                    ClearOwnership_NoLock();
                }

                if (!TrySetClipboardText(value, reasonCode))
                {
                    ClearHash(ref newHash);
                    LogEvent("SensitiveClipboardSetFailed", reasonCode);
                    return false;
                }

                lock (_sync)
                {
                    ClearOwnership_NoLock();
                    _ownedHash32 = newHash;
                    _ownedLength = fingerprint.Length;
                    newHash = null;

                    var ttl = TimeSpan.FromSeconds(AppSettingsService.GetSensitiveClipboardClearSeconds());
                    EnsureTimer_NoLock(ttl);
                    _timer!.Touch(true);
                }

                LogEvent(reasonCode, "Sensitive clipboard copy action completed.");
                return true;
            }
            catch (Exception ex)
            {
                ClearHash(ref newHash);
                lock (_sync)
                {
                    ClearOwnership_NoLock();
                    _timer?.Stop();
                }

                LogFailure("SensitiveClipboardSetFailed", reasonCode, ex);
                return false;
            }
        }

        public void ClearIfOwned()
        {
            byte[]? ownedHash;
            int ownedLength;

            lock (_sync)
            {
                if (_ownedHash32 == null || _ownedLength <= 0)
                    return;

                ownedHash = CopyHash(_ownedHash32);
                ownedLength = _ownedLength;
            }

            try
            {
                if (!TryGetClipboardText(out var current, out var readException))
                {
                    lock (_sync)
                    {
                        ClearOwnership_NoLock();
                    }

                    LogFailure("SensitiveClipboardReadFailed", "TimerElapsed", readException);
                    return;
                }

                if (string.IsNullOrEmpty(current))
                {
                    lock (_sync)
                    {
                        ClearOwnership_NoLock();
                    }

                    LogEvent("SensitiveClipboardChangedBeforeClear", "Clipboard no longer contains owned text.");
                    return;
                }

                var currentFingerprint = Fingerprint(current);
                try
                {
                    bool matches = ownedLength == currentFingerprint.Length &&
                                   FixedTimeEquals(ownedHash, currentFingerprint.Hash32);

                    if (matches)
                    {
                        if (TryClearClipboard(out var clearException))
                            LogEvent("SensitiveClipboardCleared", "Owned sensitive clipboard value cleared.");
                        else
                            LogFailure("SensitiveClipboardClearFailed", "TimerElapsed", clearException);
                    }
                    else
                    {
                        LogEvent("SensitiveClipboardChangedBeforeClear", "Clipboard changed before sensitive timer elapsed.");
                    }
                }
                finally
                {
                    var currentHash = currentFingerprint.Hash32;
                    ClearHash(ref currentHash);
                }
            }
            catch (Exception ex)
            {
                LogFailure("SensitiveClipboardClearFailed", "TimerElapsed", ex);
            }
            finally
            {
                ClearHash(ref ownedHash);
                lock (_sync)
                {
                    ClearOwnership_NoLock();
                    _timer?.Stop();
                }
            }
        }

        public void ClearNow()
        {
            try
            {
                if (TryClearClipboard(out var clearException))
                    LogEvent("SensitiveClipboardCleared", "Sensitive clipboard cleared by request.");
                else
                    LogFailure("SensitiveClipboardClearFailed", "ClearNow", clearException);
            }
            finally
            {
                lock (_sync)
                {
                    ClearOwnership_NoLock();
                    _timer?.Stop();
                }
            }
        }

        private void EnsureTimer_NoLock(TimeSpan interval)
        {
            if (_timer == null)
                _timer = new AutoHideTimer(interval, ClearIfOwned);
            else
                _timer.Interval = interval;
        }

        private static bool TrySetClipboardText(string text, string reasonCode)
        {
            return TryClipboardAction(
                () => Clipboard.SetText(text),
                "SensitiveClipboardSetFailed",
                reasonCode,
                out _);
        }

        private static bool TryGetClipboardText(out string text, out Exception? exception)
        {
            text = string.Empty;
            string capturedText = string.Empty;
            Exception? captured = null;
            bool ok = TryClipboardAction(
                () =>
                {
                    capturedText = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                },
                "SensitiveClipboardReadFailed",
                "TimerElapsed",
                out captured);

            text = capturedText;
            exception = captured;
            return ok;
        }

        private static bool TryClearClipboard(out Exception? exception)
        {
            Exception? captured = null;
            bool ok = TryClipboardAction(
                Clipboard.Clear,
                "SensitiveClipboardClearFailed",
                "TimerElapsed",
                out captured);

            exception = captured;
            return ok;
        }

        private static bool TryClipboardAction(Action action, string eventCode, string reasonCode, out Exception? exception)
        {
            _ = eventCode;
            _ = reasonCode;
            exception = null;

            for (int attempt = 0; attempt <= ClipboardRetryCount; attempt++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (ExternalException ex)
                {
                    exception = ex;
                    if (attempt == ClipboardRetryCount)
                        break;

                    Thread.Sleep(ClipboardRetryDelay);
                }
                catch (Exception ex)
                {
                    exception = ex;
                    break;
                }
            }

            return false;
        }

        private void ClearOwnership_NoLock()
        {
            if (_ownedHash32 != null)
            {
                Array.Clear(_ownedHash32, 0, _ownedHash32.Length);
                _ownedHash32 = null;
            }

            _ownedLength = 0;
        }

        private static (byte[] Hash32, int Length) Fingerprint(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            try
            {
                return (SHA256.HashData(bytes), value.Length);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static byte[] CopyHash(byte[] hash)
        {
            var copy = new byte[hash.Length];
            Buffer.BlockCopy(hash, 0, copy, 0, hash.Length);
            return copy;
        }

        private static void ClearHash(ref byte[]? hash)
        {
            if (hash == null)
                return;

            Array.Clear(hash, 0, hash.Length);
            hash = null;
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private static string NormalizeReason(string? reasonCode, string fallback)
        {
            return string.IsNullOrWhiteSpace(reasonCode) ? fallback : reasonCode.Trim();
        }

        private static void LogEvent(string eventCode, string message)
        {
            try
            {
                TemplateLogWriter.InsertRendered_BestEffort(new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = "SensitiveClipboard",
                    EventCode = NormalizeReason(eventCode, "SensitiveClipboardEvent"),
                    SubjectText = NormalizeReason(eventCode, "SensitiveClipboardEvent"),
                    MessageText = message
                });
            }
            catch
            {
            }
        }

        private static void LogFailure(string eventCode, string reasonCode, Exception? ex)
        {
            try
            {
                TemplateLogWriter.InsertRendered_BestEffort(new TemplateLogWriter.WriteRequest
                {
                    Level = "WARN",
                    Source = "SensitiveClipboard",
                    EventCode = NormalizeReason(eventCode, "SensitiveClipboardFailure"),
                    SubjectText = NormalizeReason(eventCode, "SensitiveClipboardFailure"),
                    MessageText = $"{NormalizeReason(reasonCode, "SensitiveClipboard")} failed: {ex?.GetType().Name ?? "Unknown"}"
                });
            }
            catch
            {
            }
        }
    }
}
