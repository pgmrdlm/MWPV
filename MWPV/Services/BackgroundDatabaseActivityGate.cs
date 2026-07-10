using System;
using System.Threading.Tasks;

namespace MWPV.Services
{
    public static class BackgroundDatabaseActivityGate
    {
        private static readonly object SyncRoot = new();
        private static int _activeOperations;
        private static bool _suppressed;
        private static TaskCompletionSource<bool>? _idleTcs;

        public static IDisposable? TryEnter()
        {
            lock (SyncRoot)
            {
                if (_suppressed)
                    return null;

                _activeOperations++;
                return new Lease();
            }
        }

        public static Task SuppressAndWaitForIdleAsync()
        {
            lock (SyncRoot)
            {
                _suppressed = true;
                if (_activeOperations == 0)
                    return Task.CompletedTask;

                _idleTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _idleTcs.Task;
            }
        }

        public static void Resume()
        {
            lock (SyncRoot)
            {
                _suppressed = false;
            }
        }

        public static Task<T> RunAsync<T>(Func<T> operation, T suppressedValue)
        {
            ArgumentNullException.ThrowIfNull(operation);
            return Task.Run(() =>
            {
                using var lease = TryEnter();
                return lease == null ? suppressedValue : operation();
            });
        }

        private static void Exit()
        {
            TaskCompletionSource<bool>? completed = null;
            lock (SyncRoot)
            {
                if (_activeOperations > 0)
                    _activeOperations--;

                if (_activeOperations == 0)
                {
                    completed = _idleTcs;
                    _idleTcs = null;
                }
            }

            completed?.TrySetResult(true);
        }

        private sealed class Lease : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 0)
                    Exit();
            }
        }
    }
}
