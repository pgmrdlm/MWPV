namespace MWPV.Data.Models;

/// <summary>
/// Represents a credential or category grouping for stored items.
/// </summary>
public sealed class Category
{
    /// <summary>Primary key.</summary>
    public long Category_Key { get; set; }

    /// <summary>Display name of the category.</summary>
    public string Category_Name { get; set; } = "";

    /// <summary>Optional description of the category.</summary>
    public string? Category_Description { get; set; }

    /// <summary>Whether the category is active/visible.</summary>
    public bool IsActive { get; set; } = true;
}
