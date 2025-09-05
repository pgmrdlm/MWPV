using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MWPV.Utilities.Diagnostics
{
    /// <summary>
    /// DEBUG-ONLY keybinds. Wires a global PreviewKeyDown to the main window.
    /// Shows a MessageBox immediately so we know this class is actually loaded.
    /// </summary>
    public static class DebugKeybinds
    {
#if DEBUG
        private static bool _wired;

        public static void Wire(Window root)
        {
            if (_wired || root is null) return;
            _wired = true;

            // 🔔 Proof-of-life popup — should appear once when MainWindow loads.
            MessageBox.Show("DebugKeybinds: wired", "MWPV DEBUG", MessageBoxButton.OK, MessageBoxImage.Information);

            root.PreviewKeyDown += OnPreviewKeyDown;
            Debug.WriteLine("[DBG] DebugKeybinds wired to: " + root.GetType().Name);
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // Ctrl+Shift+L => dump DB (stub for now)
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)
                    && e.Key == Key.L)
                {
                    e.Handled = true;
                    MessageBox.Show("HOTKEY: Ctrl+Shift+L captured.\n(Stub: DB dump would run here.)",
                        "MWPV DEBUG", MessageBoxButton.OK, MessageBoxImage.Information);
                    Debug.WriteLine("[DBG] Ctrl+Shift+L pressed.");
                    // TODO: call your existing dump routine here
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DBG] DebugKeybinds error: " + ex);
            }
        }
#endif
    }
}
