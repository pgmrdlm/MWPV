using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace MWPV.View.UserControls
{
    public partial class CatagoryGrid : UserControl
    {
        // >>> Event the Panel listens to <<<
        public event EventHandler<object>? CategoryPillClicked;

        public CatagoryGrid()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[CatagoryGrid.Loaded] calling RefreshCategoryGrid()");
#endif
            RefreshCategoryGrid();
        }

        /// <summary>Rebuild the pill buttons from the current categories (fan-out 1–3 per row).</summary>
        public void RefreshCategoryGrid()
        {
            try
            {
#if DEBUG
                Debug.WriteLine("[CatagoryGrid.Refresh] ENTER");
#endif
                ButtonsHost.Children.Clear();

                foreach (var row in QueryCategories())
                {
                    // Try your canonical names first
                    AddPillIfPresent(row, "strCategory1", "strCategoryToolTip1");
                    AddPillIfPresent(row, "strCategory2", "strCategoryToolTip2");
                    AddPillIfPresent(row, "strCategory3", "strCategoryToolTip3");

                    // Legacy Col/Des fallback
                    AddPillIfPresent(row, "Col1", "Des1");
                    AddPillIfPresent(row, "Col2", "Des2");
                    AddPillIfPresent(row, "Col3", "Des3");

                    // Generic fallbacks
                    AddPillIfPresent(row, "Cat1", "Desc");
                    AddPillIfPresent(row, "Name", "Description");
                    AddPillIfPresent(row, "CatName", "Description");
                    AddPillIfPresent(row, "CatagoryName", "Description");
                    AddPillIfPresent(row, "Title", "Tooltip");
                    AddPillIfPresent(row, "Category", "Tooltip");
                }

#if DEBUG
                Debug.WriteLine($"[CatagoryGrid.Refresh] EXIT — ButtonsHost.Children={ButtonsHost.Children.Count}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("[CatagoryGrid.Refresh] ERROR: " + ex);
#endif
            }
        }

        /// <summary>Show/Hide the inline “add” panel.</summary>
        public void ShowAddPanel() => AddPanel.Visibility = Visibility.Visible;
        public void HideAddPanel() => AddPanel.Visibility = Visibility.Collapsed;

        private void SubmitAddCategory_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[CatagoryGrid] SubmitAddCategory_Click — toggling back to grid");
#endif
            HideAddPanel();
        }

        // ---------- helpers ----------

        private void AddPillIfPresent(object row, string titleProp, string? tipProp = null)
        {
            string title = GetString(row, titleProp);
            if (string.IsNullOrWhiteSpace(title))
                return;

            string? tip = string.IsNullOrWhiteSpace(tipProp) ? null : GetStringOrNull(row, tipProp);

            var btn = new Button
            {
                Content = title,
                ToolTip = string.IsNullOrWhiteSpace(tip) ? null : tip
            };

            if (TryFindResource("CategoryPill") is Style pillStyle)
                btn.Style = pillStyle;

            // make long text behave
            btn.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            btn.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);

            // >>> raise event when clicked <<<
            btn.Click += (_, __) => CategoryPillClicked?.Invoke(this, row);

            ButtonsHost.Children.Add(btn);
        }

        /// <summary>
        /// Try a few likely method names on CategoryService:
        /// SelectCatagories / LoadCatagories / SelectCategories / GetCategories (public static, parameterless)
        /// </summary>
        private static IEnumerable QueryCategories()
        {
            var t = typeof(MWPV.Services.CategoryService);
            MethodInfo? m =
                t.GetMethod("SelectCatagories", BindingFlags.Public | BindingFlags.Static) ??
                t.GetMethod("LoadCatagories", BindingFlags.Public | BindingFlags.Static) ??
                t.GetMethod("SelectCategories", BindingFlags.Public | BindingFlags.Static) ??
                t.GetMethod("GetCategories", BindingFlags.Public | BindingFlags.Static);

            if (m == null)
            {
#if DEBUG
                Debug.WriteLine("[CatagoryGrid.QueryCategories] No compatible method found on CategoryService.");
#endif
                return Array.Empty<object>();
            }

            try
            {
                var result = m.Invoke(null, null);
                return (result as IEnumerable) ?? Array.Empty<object>();
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("[CatagoryGrid.QueryCategories] Invoke failed: " + ex);
#endif
                return Array.Empty<object>();
            }
        }

        private static string GetString(object obj, string name)
        {
            var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p != null && p.PropertyType == typeof(string))
            {
                var s = (string?)p.GetValue(obj);
                if (!string.IsNullOrWhiteSpace(s)) return s!;
            }
            return string.Empty;
        }

        private static string? GetStringOrNull(object obj, string name)
        {
            var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p != null && p.PropertyType == typeof(string))
            {
                var s = (string?)p.GetValue(obj);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }
    }
}
