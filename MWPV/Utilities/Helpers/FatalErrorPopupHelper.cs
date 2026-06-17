using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Utilities.Helpers;
using MWPV.View.UserControls.Popup;
using MWPV.Services.AppLifecycle;

namespace Utilities.Helpers
{
    public static class FatalErrorPopupHelper
    {
        private const string FatalTitleText = "The application encountered a fatal error and must close.";
        private const string DefaultMessageText = "A fatal error occurred.";
        private const string PrimaryExitText = "OK / Exit";
        private const string SecondaryCopyExitText = "Copy / Exit";

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

            var hostWindow = CreateHostWindow(popup);
            PopupDialog.PopupResult result = PopupDialog.PopupResult.Abort;

            try
            {
                popup.Completed += OnCompleted;
                hostWindow.ShowDialog();
            }
            catch
            {
                TerminateApplication();
                return;
            }
            finally
            {
                popup.Completed -= OnCompleted;
                try
                {
                    if (hostWindow.IsVisible)
                        hostWindow.Close();
                }
                catch
                {
                    // Best-effort close only.
                }
            }

            if (result == PopupDialog.PopupResult.Cancel)
                TryCopyToClipboard(copyPayload);

            TerminateApplication();

            void OnCompleted(PopupDialog.PopupResult popupResult)
            {
                result = popupResult;
                try
                {
                    hostWindow.DialogResult = false;
                }
                catch
                {
                    try { hostWindow.Close(); } catch { }
                }
            }
        }

        private static Window CreateHostWindow(PopupDialog popup)
        {
            return new Window
            {
                Title = "MWPV Fatal Error",
                Content = popup,
                Owner = TryGetOwnerWindow(),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Topmost = true,
                Background = System.Windows.Media.Brushes.Transparent,
                AllowsTransparency = true
            };
        }

        private static Window? TryGetOwnerWindow()
        {
            if (Application.Current == null)
                return null;

            foreach (Window window in Application.Current.Windows)
            {
                if (window.IsActive)
                    return window;
            }

            return Application.Current.MainWindow;
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
                AppExit.Shutdown(Application.Current, AppExitCode.UnhandledFatalError, "Fatal error popup closed.");
            }
            catch
            {
                Environment.Exit((int)AppExitCode.UnhandledFatalError);
            }
        }
    }
}
