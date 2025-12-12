using System;
using System.Windows.Threading;

namespace MWPV.Utilities.Helpers
{
    /// <summary>
    /// Simple "touch-to-keep-alive" UI timer.
    /// Call Touch(true) whenever sensitive data is currently revealed.
    /// Call Touch(false) (or Stop) when nothing is revealed.
    /// On timeout, it fires the callback (typically: hide everything + wipe plain text overlays).
    /// </summary>
    public sealed class AutoHideTimer : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _onTimeout;
        private bool _disposed;

        public AutoHideTimer(TimeSpan interval, Action onTimeout, DispatcherPriority priority = DispatcherPriority.Background)
        {
            _onTimeout = onTimeout ?? throw new ArgumentNullException(nameof(onTimeout));

            _timer = new DispatcherTimer(priority)
            {
                Interval = interval
            };

            _timer.Tick += Timer_Tick;
        }

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public bool IsRunning => _timer.IsEnabled;

        /// <summary>
        /// Stop, then restart only if shouldRun is true.
        /// </summary>
        public void Touch(bool shouldRun)
        {
            ThrowIfDisposed();

            _timer.Stop();
            if (shouldRun)
            {
                _timer.Start();
            }
        }

        public void Stop()
        {
            if (_disposed) return;
            _timer.Stop();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Stop first so the callback can safely call Touch(...) without re-entrancy surprises.
            _timer.Stop();
            _onTimeout();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _timer.Tick -= Timer_Tick;
            _timer.Stop();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoHideTimer));
        }
    }
}
