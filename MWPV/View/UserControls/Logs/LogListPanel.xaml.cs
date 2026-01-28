using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class LogListPanel : UserControl
    {
        public LogListPanel()
        {
            InitializeComponent();
        }

        // Dependency Properties
        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(LogListPanel), new PropertyMetadata(null));

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(LogListPanel),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }
        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(LogListPanel),
                new PropertyMetadata("Page 1 • Showing 15 rows"));

        // Routed Events (Prev/Next + SelectionChanged only)
        public static readonly RoutedEvent PrevRequestedEvent =
            EventManager.RegisterRoutedEvent(nameof(PrevRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(LogListPanel));
        public event RoutedEventHandler PrevRequested { add => AddHandler(PrevRequestedEvent, value); remove => RemoveHandler(PrevRequestedEvent, value); }

        public static readonly RoutedEvent NextRequestedEvent =
            EventManager.RegisterRoutedEvent(nameof(NextRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(LogListPanel));
        public event RoutedEventHandler NextRequested { add => AddHandler(NextRequestedEvent, value); remove => RemoveHandler(NextRequestedEvent, value); }

        public static readonly RoutedEvent SelectionChangedRoutedEvent =
            EventManager.RegisterRoutedEvent(nameof(SelectionChangedRouted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(LogListPanel));
        public event RoutedEventHandler SelectionChangedRouted { add => AddHandler(SelectionChangedRoutedEvent, value); remove => RemoveHandler(SelectionChangedRoutedEvent, value); }

        // Handlers (raise events)
        private void _btnPrev_Click(object sender, RoutedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(PrevRequestedEvent));

        private void _btnNext_Click(object sender, RoutedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(NextRequestedEvent));

        private void dgLogs_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(SelectionChangedRoutedEvent));
    }
}
