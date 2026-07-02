// File: Models/CategoryItemGriud.cs
//
// FULL REWRITE
//
// Purpose:
// - Row model backing the 3-column CategoryItemGrid.
// - Each row can show up to 3 “pill” buttons (Col1/Col2/Col3) with tooltips and keys.
//
// Notes:
// - Keep property names as-is (bindings already reference them in XAML).
// - Default empty strings avoid null binding surprises.
// - Yes, the class name has the historical typo "Griud" — we keep it to avoid churn.

#nullable enable
namespace MWPV.Models
{
    public sealed class CategoryItemGriud
    {
        // Pill text (what the user sees)
        public string strCategoryItem1 { get; set; } = string.Empty;
        public string? strCategoryItem2 { get; set; }
        public string? strCategoryItem3 { get; set; }

        // Tooltip text (usually description/notes)
        public string? strCategoryItemToolTip1 { get; set; }
        public string? strCategoryItemToolTip2 { get; set; }
        public string? strCategoryItemToolTip3 { get; set; }

        // Key/Tag values (usually PKs as strings)
        public string? strCategoryItemKey1 { get; set; }
        public string? strCategoryItemKey2 { get; set; }
        public string? strCategoryItemKey3 { get; set; }

        // Active state for visual styling. Null/missing is treated as active.
        public bool? IsActive1 { get; set; }
        public bool? IsActive2 { get; set; }
        public bool? IsActive3 { get; set; }
    }
}
