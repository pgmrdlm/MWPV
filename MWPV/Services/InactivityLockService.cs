// File: MWPV/Services/InactivityLockService.cs
// Purpose:
// - Owns the configured inactivity timer (MWPV-scoped)
// - Resets on App.UserActivityDetected (keystrokes/mouse clicks inside MWPV)
// - On timeout: runs existing "Cancel" logic (via injected callback) and then runs a lock callback (also injected)
//
// Notes:
// - This class is intentionally dumb: it does not know about tabs, viewmodels, or Windows Hello.
// - MainWindow (or your top-level UI controller) wires in the two callbacks.
// - No encryption, no persistence, no extra state.

using System;
using System.Threading;
using System.Windows.Threading;
using Utilities.Helpers;

namespace MWPV.Services
{
    internal sealed class InactivityLockService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private TimeSpan _timeout;

        private readonly Func<bool> _isSensitiveContextOpen;
        private readonly Action _forceCancelSensitiveContext;
        private readonly Action _lockAction;

        private bool _started;
        private bool _disposed;

        public InactivityLockService(
            TimeSpan timeout,
            Func<bool> isSensitiveContextOpen,
            Action forceCancelSensitiveContext,
            Action lockAction)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be > 0.");

            _timeout = timeout;
            _isSensitiveContextOpen = isSensitiveContextOpen ?? throw new ArgumentNullException(nameof(isSensitiveContextOpen));
            _forceCancelSensitiveContext = forceCancelSensitiveContext ?? throw new ArgumentNullException(nameof(forceCancelSensitiveContext));
            _lockAction = lockAction ?? throw new ArgumentNullException(nameof(lockAction));

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = _timeout
            };
            _timer.Tick += OnTimerTick;

        }

        public TimeSpan Timeout => _timeout;

        public void UpdateTimeout(TimeSpan timeout)
        {
            ThrowIfDisposed();
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be > 0.");

            _timeout = timeout;
            _timer.Interval = timeout;
            if (_started)
                Reset();
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (_started) return;

            // Hook app-scoped activity signal
            App.UserActivityDetected += OnUserActivityDetected;

            _started = true;

            Reset(); // start countdown immediately
        }

        public void Stop()
        {
            if (_disposed) return;
            if (!_started) return;

            try { App.UserActivityDetected -= OnUserActivityDetected; } catch { /* swallow */ }
            try { _timer.Stop(); } catch { /* swallow */ }

            _started = false;

        }

        public void Reset()
        {
            ThrowIfDisposed();
            if (!_started) return;

            // Restart countdown
            try { _timer.Stop(); } catch { /* swallow */ }
            _timer.Interval = _timeout;
            _timer.Start();

        }

        private void OnUserActivityDetected()
        {
            // Any keystroke/mouse click/wheel inside MWPV resets the timer.
            // Keep it simple and cheap.
            try { Reset(); } catch { /* swallow */ }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            // Stop first to avoid re-entrancy loops while we run actions.
            try { _timer.Stop(); } catch { /* swallow */ }

            bool sensitiveOpen = false;

            try
            {
                sensitiveOpen = _isSensitiveContextOpen();

                // If the sensitive context (Basic tab) is open, force the existing Cancel path.
                if (sensitiveOpen)
                {
                    _forceCancelSensitiveContext();
                }
            }
            catch
            {
                // Never crash on timeout handling.
            }

            try
            {
                // Lock action (your Hello gate / lock UI) is injected by caller.
                _lockAction();
            }
            catch (Exception ex)
            {
                _ = FatalErrorPopupHelper.ShowFatalAsync(
                    "MWPV encountered a fatal error while handling inactivity timeout and must close.",
                    ex,
                    "The inactivity lock action failed after timeout handling reached the final shutdown/lock stage.");
            }

            // After lock is initiated, we do NOT automatically restart the timer here.
            // The caller should restart/reset after unlock (or keep it stopped while locked),
            // depending on your lock UX.
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(InactivityLockService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();

            try { _timer.Tick -= OnTimerTick; } catch { /* swallow */ }
        }
    }
}
