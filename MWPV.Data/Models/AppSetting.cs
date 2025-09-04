namespace MWPV.Data.Models;

/// <summary>
/// Application-level setting persisted in the database.
/// </summary>
public sealed class AppSetting
{
    public long AppSetting_Key { get; set; }
    public string Setting_Name { get; set; } = "";
    public string Setting_Value { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
