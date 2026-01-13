// File: View/UserControls/CategoryItems/CategoryItemBasicEnums.cs
using System;

namespace MWPV.View.UserControls.CategoryItems
{
    /// <summary>
    /// Basic-tab-only enums. Do not reuse outside CategoryItemBasicPanel unless we explicitly promote them later.
    /// </summary>

    [Flags]
    public enum BasicChangeFlags
    {
        None = 0,

        Name = 1 << 0,
        Password = 1 << 1,
        Pin = 1 << 2,
        UserName = 1 << 3,
        Url = 1 << 4,
        Phone = 1 << 5,
        Email = 1 << 6,
        Notes = 1 << 7,

        BookmarkOnly = 1 << 8,

        Any = Name | Password | Pin | UserName | Url | Phone | Email | Notes | BookmarkOnly
    }

    public enum BasicUiMode
    {
        View = 0,
        Edit = 1
    }

    public enum BasicField
    {
        None = 0,

        Name,
        Password,
        Pin,
        UserName,
        Url,
        Phone,
        Email,
        Notes,
        BookmarkOnly
    }

    public enum SensitiveTarget
    {
        None = 0,

        Password,
        Pin,
        Phone,
        Email
    }

    public enum MaskState
    {
        Masked = 0,
        Revealed = 1
    }
}
