using System;
using System.Windows;
using System.Windows.Threading;
using MWPV.View.UserControls.Popup;

namespace Utilities.Helpers
{
    public static class UpgradeCloseWarningPopupHelper
    {
        private const string TitleText = "Upgrade Still In Progress";
        private const string MessageText =
            "MWPV is still completing an upgrade. If you close the app now, the installation may not finish correctly and MWPV may not work properly until the upgrade is completed.\n\n" +
            "Do you want to close MWPV anyway?";

        public static bool Show(Window? owner)
        {
            var dispatcher = Application.Current?.Dispatcher ?? owner?.Dispatcher;
            if (dispatcher == null)
                return false;

            if (dispatcher.CheckAccess())
                return ShowCore(owner);

            try
            {
                return dispatcher.Invoke(() => ShowCore(owner), DispatcherPriority.Send);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShowCore(Window? owner)
        {
            try
            {
                var result = PopupDialog.PopupResult.Cancel;
                var popup = new PopupDialog
                {
                    EnterResult = PopupDialog.PopupResult.Cancel,
                    InitialFocusResult = PopupDialog.PopupResult.Cancel
                };

                popup.Configure(
                    severity: 0,
                    title: TitleText,
                    message: MessageText,
                    showCancel: true,
                    primaryText: "Yes",
                    secondaryText: "No");

                var hostWindow = new Window
                {
                    Title = TitleText,
                    Content = popup,
                    Owner = owner ?? TryGetOwnerWindow(),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    AllowsTransparency = true
                };

                popup.Completed += popupResult =>
                {
                    result = popupResult;
                    try { hostWindow.DialogResult = false; }
                    catch { try { hostWindow.Close(); } catch { } }
                };

                try { hostWindow.ShowDialog(); }
                catch { return false; }

                return result == PopupDialog.PopupResult.Accept;
            }
            catch
            {
                return false;
            }
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
