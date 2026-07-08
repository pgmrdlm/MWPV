using System;
using System.Collections.Generic;

namespace MWPV.Services
{
    public enum AppStatusMessageKind
    {
        Info,
        Success,
        Warning
    }

    public sealed record AppStatusMessage(
        string Text,
        AppStatusMessageKind Kind,
        TimeSpan? AutoClearAfter,
        bool ClearOnUserInput);

    public static class AppStatusMessageService
    {
        private static readonly object SyncRoot = new();
        private static readonly List<Action<AppStatusMessage>> Subscribers = new();
        private static AppStatusMessage? _pendingMessage;

        public static IDisposable Subscribe(Action<AppStatusMessage> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            AppStatusMessage? pending;
            lock (SyncRoot)
            {
                Subscribers.Add(handler);
                pending = _pendingMessage;
                _pendingMessage = null;
            }

            if (pending != null)
                InvokeSubscriber(handler, pending);

            return new Subscription(handler);
        }

        public static void Publish(
            string text,
            AppStatusMessageKind kind = AppStatusMessageKind.Info,
            TimeSpan? autoClearAfter = null,
            bool clearOnUserInput = true)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var message = new AppStatusMessage(text.Trim(), kind, autoClearAfter, clearOnUserInput);
            Action<AppStatusMessage>[] subscribers;

            lock (SyncRoot)
            {
                if (Subscribers.Count == 0)
                {
                    _pendingMessage = message;
                    return;
                }

                _pendingMessage = null;
                subscribers = Subscribers.ToArray();
            }

            foreach (var subscriber in subscribers)
                InvokeSubscriber(subscriber, message);
        }

        private static void Unsubscribe(Action<AppStatusMessage> handler)
        {
            lock (SyncRoot)
            {
                Subscribers.Remove(handler);
            }
        }

        private static void InvokeSubscriber(Action<AppStatusMessage> handler, AppStatusMessage message)
        {
            try { handler(message); }
            catch { /* Status publishing must never destabilize the app. */ }
        }

        private sealed class Subscription : IDisposable
        {
            private Action<AppStatusMessage>? _handler;

            public Subscription(Action<AppStatusMessage> handler)
            {
                _handler = handler;
            }

            public void Dispose()
            {
                var handler = _handler;
                if (handler == null)
                    return;

                _handler = null;
                Unsubscribe(handler);
            }
        }
    }
}
