using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MWPV.View.UserControls;
using MWPV.View.UserControls.Popup;

namespace MWPV.Utilities.Helpers
{
    public static class FatalErrorPopupHelper
    {
        private const string FatalTitleText = "The application encountered a fatal error and must close.";
        private const string DefaultMessageText = "A fatal error occurred.";
        private const string PrimaryExitText = "OK / Exit";
        private const string SecondaryCopyExitText = "Copy / Exit";
        private const int FatalExitCode = -1;

        public static Task ShowFatalAsync(string message, Exception? exception = null, string? details = null)
        {
            string displayMessage = BuildDisplayMessage(message);
            string copyPayload = BuildCopyPayload(displayMessage, exception, details);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                TerminateApplication();
                return Task.CompletedTask;
            }

            if (dispatcher.CheckAccess())
                return ShowFatalCoreAsync(displayMessage, copyPayload);

            return dispatcher.InvokeAsync(
                () => ShowFatalCoreAsync(displayMessage, copyPayload),
                DispatcherPriority.Send).Task.Unwrap();
        }

        private static async Task ShowFatalCoreAsync(string displayMessage, string copyPayload)
        {
            var popupHost = TryGetPopupHost();
            if (popupHost == null)
            {
                TerminateApplication();
                return;
            }

            var popup = new PopupDialog
            {
                IsFatalError = true
            };

            popup.Configure(
                severity: 1,
                title: FatalTitleText,
                message: displayMessage,
                showCancel: true,
                primaryText: PrimaryExitText,
                secondaryText: SecondaryCopyExitText);

            PopupDialog.PopupResult result;
            try
            {
                result = await popupHost.ShowPopupAsync(popup);
            }
            catch
            {
                TerminateApplication();
                return;
            }

            if (result == PopupDialog.PopupResult.Cancel)
                TryCopyToClipboard(copyPayload);

            TerminateApplication();
        }

        private static Panel? TryGetPopupHost()
        {
            if (Application.Current?.MainWindow is MainWindow mainWindow && mainWindow.Panel != null)
                return mainWindow.Panel;

            if (Application.Current == null)
                return null;

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow candidate && candidate.Panel != null)
                    return candidate.Panel;
            }

            return null;
        }

        private static string BuildDisplayMessage(string message)
        {
            return string.IsNullOrWhiteSpace(message)
                ? DefaultMessageText
                : message.Trim();
        }

        private static string BuildCopyPayload(string displayMessage, Exception? exception, string? details)
        {
            var builder = new StringBuilder();

            builder.AppendLine("MWPV Fatal Application Error");
            builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
            builder.AppendLine();
            builder.AppendLine("User message:");
            builder.AppendLine(displayMessage);

            if (!string.IsNullOrWhiteSpace(details))
            {
                builder.AppendLine();
                builder.AppendLine("Details:");
                builder.AppendLine(details.Trim());
            }

            if (exception != null)
            {
                builder.AppendLine();
                builder.AppendLine("Exception:");
                builder.AppendLine(exception.ToString());
            }

            return builder.ToString().TrimEnd();
        }

        private static void TryCopyToClipboard(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() => Clipboard.SetText(payload), DispatcherPriority.Send);
                    return;
                }

                Clipboard.SetText(payload);
            }
            catch
            {
                // Best-effort. App still exits.
            }
        }

        private static void TerminateApplication()
        {
            try
            {
                Application.Current?.Shutdown(FatalExitCode);
            }
            catch
            {
                Environment.Exit(FatalExitCode);
            }
        }
    }
}
