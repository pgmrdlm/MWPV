// File: MWPV/Services/InactivityLockService.cs
// Purpose:
// - Owns the 5-minute inactivity timer (MWPV-scoped)
// - Resets on App.UserActivityDetected (keystrokes/mouse clicks inside MWPV)
// - On timeout: runs existing "Cancel" logic (via injected callback) and then runs a lock callback (also injected)
//
// Notes:
// - This class is intentionally dumb: it does not know about tabs, viewmodels, or Windows Hello.
// - MainWindow (or your top-level UI controller) wires in the two callbacks.
// - No encryption, no persistence, no extra state.

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace MWPV.Services
{
    internal sealed class InactivityLockService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly TimeSpan _timeout;

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

#if DEBUG
            Log($"CTOR timeout={_timeout.TotalSeconds:0}s priority=Background dispatcher={Dispatcher.CurrentDispatcher.Thread.ManagedThreadId}");
#endif
        }

        public TimeSpan Timeout => _timeout;

        public void Start()
        {
            ThrowIfDisposed();
            if (_started) return;

            // Hook app-scoped activity signal
            App.UserActivityDetected += OnUserActivityDetected;

            _started = true;

#if DEBUG
            Log("START (hooked App.UserActivityDetected)");
#endif

            Reset(); // start countdown immediately
        }

        public void Stop()
        {
            if (_disposed) return;
            if (!_started) return;

            try { App.UserActivityDetected -= OnUserActivityDetected; } catch { /* swallow */ }
            try { _timer.Stop(); } catch { /* swallow */ }

            _started = false;

#if DEBUG
            Log("STOP (unhooked App.UserActivityDetected, timer stopped)");
#endif
        }

        public void Reset()
        {
            ThrowIfDisposed();
            if (!_started) return;

            // Restart countdown
            try { _timer.Stop(); } catch { /* swallow */ }
            _timer.Interval = _timeout;
            _timer.Start();

#if DEBUG
            Log($"RESET (timer restarted, interval={_timeout.TotalSeconds:0}s)");
#endif
        }

        private void OnUserActivityDetected()
        {
#if DEBUG
            Log("ACTIVITY (App.UserActivityDetected) -> Reset()");
#endif
            // Any keystroke/mouse click/wheel inside MWPV resets the timer.
            // Keep it simple and cheap.
            try { Reset(); } catch { /* swallow */ }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            // Stop first to avoid re-entrancy loops while we run actions.
            try { _timer.Stop(); } catch { /* swallow */ }

#if DEBUG
            Log("TICK (timeout reached) ENTER");
#endif

            bool sensitiveOpen = false;

            try
            {
                sensitiveOpen = _isSensitiveContextOpen();

#if DEBUG
                Log($"TICK isSensitiveContextOpen() => {sensitiveOpen}");
#endif

                // If the sensitive context (Basic tab) is open, force the existing Cancel path.
                if (sensitiveOpen)
                {
#if DEBUG
                    Log("TICK calling forceCancelSensitiveContext()");
#endif
                    _forceCancelSensitiveContext();

#if DEBUG
                    Log("TICK forceCancelSensitiveContext() returned");
#endif
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Log("TICK ERROR during sensitive handling: " + ex.GetType().Name + " " + ex.Message);
#endif
                // Never crash on timeout handling.
            }

            try
            {
#if DEBUG
                Log("TICK calling lockAction()");
#endif
                // Lock action (your Hello gate / lock UI) is injected by caller.
                _lockAction();

#if DEBUG
                Log("TICK lockAction() returned");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Log("TICK ERROR during lockAction: " + ex.GetType().Name + " " + ex.Message);
#endif
                // swallow
            }

#if DEBUG
            Log("TICK EXIT (timer remains stopped until caller resets after unlock)");
#endif

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

#if DEBUG
            Log("DISPOSE ENTER");
#endif

            Stop();

            try { _timer.Tick -= OnTimerTick; } catch { /* swallow */ }

#if DEBUG
            Log("DISPOSE EXIT");
#endif
        }

#if DEBUG
        private static void Log(string message)
        {
            // Keep this dead-simple: one line per event, easy to grep in Output.
            Debug.WriteLine($"[INACTIVITY] {DateTime.Now:HH:mm:ss.fff} [T{Thread.CurrentThread.ManagedThreadId}] {message}");
        }
#endif
    }
}
