// File: View/UserControls/Popup/PopupDialog.xaml.cs
//
// PURPOSE
// - Code-behind for a generic modal popup overlay UserControl.
// - Caller configures severity + title + message + buttons.
// - Returns a value to the core app through an event (Completed).
//
// SEVERITY (per your rule)
// - 0 = Warning (yellow)
// - 1 = Abort   (red)
//
// BUTTON MODES
// - Warning: Accept / Cancel
// - Abort:   Abort only
//
// IMPORTANT
// - This control does NOT decide how to show/hide itself.
//   The host (MainWindow/Panel overlay layer) should add/remove it from the visual tree.
// - When completed, host receives PopupResult and can respond accordingly.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MWPV.View.UserControls.Popup
{
    public partial class PopupDialog : UserControl
    {
        // =========================
        // Result contract
        // =========================
        public enum PopupResult
        {
            Accept = 0,
            Cancel = 1,
            Abort = 2
        }

        // =========================
        // Event: host listens
        // =========================
        public event Action<PopupResult>? Completed;

        // =========================
        // Bindable properties
        // =========================
        public static readonly DependencyProperty SeverityProperty =
            DependencyProperty.Register(nameof(Severity), typeof(int), typeof(PopupDialog),
                new PropertyMetadata(0, OnSeverityChanged));

        public static readonly DependencyProperty TitleTextProperty =
            DependencyProperty.Register(nameof(TitleText), typeof(string), typeof(PopupDialog),
                new PropertyMetadata(""));

        public static readonly DependencyProperty MessageTextProperty =
            DependencyProperty.Register(nameof(MessageText), typeof(string), typeof(PopupDialog),
                new PropertyMetadata(""));

        public static readonly DependencyProperty PrimaryButtonTextProperty =
            DependencyProperty.Register(nameof(PrimaryButtonText), typeof(string), typeof(PopupDialog),
                new PropertyMetadata("Accept"));

        public static readonly DependencyProperty SecondaryButtonTextProperty =
            DependencyProperty.Register(nameof(SecondaryButtonText), typeof(string), typeof(PopupDialog),
                new PropertyMetadata("Cancel"));

        public static readonly DependencyProperty IsSecondaryVisibleProperty =
            DependencyProperty.Register(nameof(IsSecondaryVisible), typeof(bool), typeof(PopupDialog),
                new PropertyMetadata(true, OnSecondaryVisibilityChanged));

        public static readonly DependencyProperty IsFatalErrorProperty =
            DependencyProperty.Register(nameof(IsFatalError), typeof(bool), typeof(PopupDialog),
                new PropertyMetadata(false, OnFatalErrorChanged));

        public int Severity
        {
            get => (int)GetValue(SeverityProperty);
            set => SetValue(SeverityProperty, value);
        }

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }

        public string MessageText
        {
            get => (string)GetValue(MessageTextProperty);
            set => SetValue(MessageTextProperty, value);
        }

        public string PrimaryButtonText
        {
            get => (string)GetValue(PrimaryButtonTextProperty);
            set => SetValue(PrimaryButtonTextProperty, value);
        }

        public string SecondaryButtonText
        {
            get => (string)GetValue(SecondaryButtonTextProperty);
            set => SetValue(SecondaryButtonTextProperty, value);
        }

        public bool IsSecondaryVisible
        {
            get => (bool)GetValue(IsSecondaryVisibleProperty);
            set => SetValue(IsSecondaryVisibleProperty, value);
        }

        public bool IsFatalError
        {
            get => (bool)GetValue(IsFatalErrorProperty);
            set => SetValue(IsFatalErrorProperty, value);
        }

        // =========================
        // Ctor
        // =========================
        public PopupDialog()
        {
            InitializeComponent();
            DataContext = this;

            // Default visuals
            ApplySeverityVisuals();
            ApplySecondaryVisibility();
            ApplyFatalState();

            // Keyboard: make sure it feels "forced"
            Loaded += (_, __) =>
            {
                Focus();
                Keyboard.Focus(this);
            };
        }

        // =========================
        // Public configurator
        // =========================
        public void Configure(
            int severity,
            string title,
            string message,
            bool showCancel,
            string primaryText,
            string? secondaryText = "Cancel")
        {
            Severity = severity;
            TitleText = title ?? string.Empty;
            MessageText = message ?? string.Empty;

            PrimaryButtonText = string.IsNullOrWhiteSpace(primaryText) ? "OK" : primaryText;

            IsSecondaryVisible = showCancel;
            SecondaryButtonText = string.IsNullOrWhiteSpace(secondaryText) ? "Cancel" : secondaryText;

            ApplySeverityVisuals();
            ApplySecondaryVisibility();
            ApplyFatalState();
        }

        // Convenience helpers that match our expected usage:
        public void ConfigureWarningAcceptCancel(string title, string message)
            => Configure(0, title, message, showCancel: true, primaryText: "Accept", secondaryText: "Cancel");

        public void ConfigureAbortOnly(string title, string message)
            => Configure(1, title, message, showCancel: false, primaryText: "Abort", secondaryText: null);

        // =========================
        // Severity visuals
        // =========================
        private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PopupDialog p)
                p.ApplySeverityVisuals();
        }

        private void ApplySeverityVisuals()
        {
            // Severity: 0=Warning, 1=Abort
            bool isAbort = Severity == 1;

            IconWarn.Visibility = isAbort ? Visibility.Collapsed : Visibility.Visible;
            IconAbort.Visibility = isAbort ? Visibility.Visible : Visibility.Collapsed;
        }

        // =========================
        // Button visibility
        // =========================
        private static void OnSecondaryVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PopupDialog p)
                p.ApplySecondaryVisibility();
        }

        private void ApplySecondaryVisibility()
        {
            btnSecondary.Visibility = IsSecondaryVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void OnFatalErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PopupDialog p)
                p.ApplyFatalState();
        }

        private void ApplyFatalState()
        {
            if (FatalHeaderHost != null)
                FatalHeaderHost.Visibility = IsFatalError ? Visibility.Visible : Visibility.Collapsed;
        }

        // =========================
        // Button click handlers
        // =========================
        private void btnPrimary_Click(object sender, RoutedEventArgs e)
        {
            // Severity 1 + Abort-only should return Abort
            if (Severity == 1 && !IsSecondaryVisible)
            {
                RaiseCompleted(PopupResult.Abort);
                return;
            }

            RaiseCompleted(PopupResult.Accept);
        }

        private void btnSecondary_Click(object sender, RoutedEventArgs e)
        {
            RaiseCompleted(PopupResult.Cancel);
        }

        // =========================
        // Enforced behavior
        // =========================
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // ESC behavior:
            // - Warning: ESC = Cancel
            // - Abort:   ESC = Abort (or ignored; we choose Abort so host can terminate cleanly)
            if (e.Key == Key.Escape)
            {
                e.Handled = true;

                if (Severity == 1 && !IsSecondaryVisible)
                    RaiseCompleted(PopupResult.Abort);
                else
                    RaiseCompleted(PopupResult.Cancel);
            }

            // ENTER behavior:
            // - Press primary action
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                btnPrimary_Click(this, new RoutedEventArgs());
            }
        }

        private void RaiseCompleted(PopupResult result)
        {
            try
            {
                Completed?.Invoke(result);
            }
            catch
            {
                // Swallow: popup should never crash the app.
            }
        }
    }
}
