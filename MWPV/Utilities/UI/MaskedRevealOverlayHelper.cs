using System.Windows;
using System.Windows.Controls;

namespace MWPV.Utilities.UI
{
    internal static class MaskedRevealOverlayHelper
    {
        public static void ShowPlainOverlay(PasswordBox maskedBox, TextBox plainBox, string? plainValue)
        {
            plainBox.Text = plainValue ?? string.Empty;
            plainBox.Visibility = Visibility.Visible;
            maskedBox.Visibility = Visibility.Collapsed;
        }

        public static void RestoreMaskedOverlay(PasswordBox maskedBox, TextBox plainBox)
        {
            plainBox.Visibility = Visibility.Collapsed;
            maskedBox.Visibility = Visibility.Visible;
        }
    }
}
