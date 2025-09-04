// UI/IconGlyph.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MWPV.UI
{
    public static class IconGlyph
    {
        // Hex like "&#xE710;" or raw "\uE710" works as content text.
        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.RegisterAttached(
                "Glyph", typeof(string), typeof(IconGlyph),
                new PropertyMetadata(null));

        public static void SetGlyph(DependencyObject element, string value) => element.SetValue(GlyphProperty, value);
        public static string GetGlyph(DependencyObject element) => (string)element.GetValue(GlyphProperty);

        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.RegisterAttached(
                "Size", typeof(double), typeof(IconGlyph),
                new PropertyMetadata(14.0));

        public static void SetSize(DependencyObject element, double value) => element.SetValue(SizeProperty, value);
        public static double GetSize(DependencyObject element) => (double)element.GetValue(SizeProperty);

        public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.RegisterAttached(
                "Spacing", typeof(Thickness), typeof(IconGlyph),
                new PropertyMetadata(new Thickness(0, 0, 8, 0)));

        public static void SetSpacing(DependencyObject element, Thickness value) => element.SetValue(SpacingProperty, value);
        public static Thickness GetSpacing(DependencyObject element) => (Thickness)element.GetValue(SpacingProperty);
    }
}
