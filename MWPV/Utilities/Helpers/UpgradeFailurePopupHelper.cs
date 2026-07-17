using System.Text;
using System.Windows;
using System.Windows.Threading;
using MWPV.Services.Upgrade;
using MWPV.View.UserControls.Popup;

namespace Utilities.Helpers
{
    public static class UpgradeFailurePopupHelper
    {
        public static void Show(UpgradeResult result)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            if (dispatcher.CheckAccess())
            {
                ShowCore(result);
                return;
            }

            dispatcher.Invoke(() => ShowCore(result), DispatcherPriority.Send);
        }

        private static void ShowCore(UpgradeResult result)
        {
            var popup = new PopupDialog
            {
                IsFatalError = true,
                FatalHeaderText = "UPGRADE FAILED"
            };

            popup.Configure(
                severity: 1,
                title: "MWPV encountered an error during upgrade.",
                message: BuildMessage(result),
                showCancel: false,
                primaryText: "OK",
                secondaryText: null);

            var hostWindow = new Window
            {
                Title = "MWPV Upgrade Failed",
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

            popup.Completed += _ =>
            {
                try { hostWindow.DialogResult = false; }
                catch { try { hostWindow.Close(); } catch { } }
            };

            try { hostWindow.ShowDialog(); }
            catch { }
        }

        private static string BuildMessage(UpgradeResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("The upgrade did not complete.");
            builder.AppendLine();
            builder.AppendLine("Open Help > Recovery and follow the manual recovery instructions before trying again.");
            builder.AppendLine("Restore both the vault database and the .pv key file from the verified upgrade backup when Help directs you to do so.");
            return builder.ToString().TrimEnd();
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

    }
}
