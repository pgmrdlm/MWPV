// File: Utilities/Helpers/CategoryItemSecure.cs
using MWPV.Models;
using System;
using Utilities.Helpers; // CategoryItemDetailJson, SecureDataValidator

namespace Utilities.Helpers
{
    internal static class CategoryItemSecure
    {
        /// <summary>Get parsed + normalized SecureData from the item's JSON (never null).</summary>
        public static SecureData GetSecure(this CategoryItemDetail item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var data = CategoryItemDetailJson.FromJson(item.SecureDataJson)
                                            .NormalizeAndValidate(); // trims + single-primary
            return data;
        }

        /// <summary>Set SecureData onto the item (normalize → serialize → store).</summary>
        public static void SetSecure(this CategoryItemDetail item, SecureData data)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            data = (data ?? new SecureData()).NormalizeAndValidate();
            item.SecureDataJson = CategoryItemDetailJson.ToJson(data);
        }

        /// <summary>Ensure a brand-new item has a minimal secure payload JSON.</summary>
        public static void EnsureSecureJson(this CategoryItemDetail item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.SecureDataJson))
                item.SecureDataJson = CategoryItemDetailJson.CreateEmptyJson();
        }
    }
}
