namespace MWPV.Data.Models;

public sealed class Category
{
    public long Category_Key { get; set; }          // PK
    public string Category_Name { get; set; } = "";
    public string? Category_Description { get; set; }
    public bool IsActive { get; set; } = true;
}
