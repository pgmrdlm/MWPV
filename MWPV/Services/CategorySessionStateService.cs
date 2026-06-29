using System.Collections.Generic;

namespace MWPV.Services
{
    /// <summary>
    /// Process-local category state that is discarded when the application exits.
    /// </summary>
    public static class CategorySessionStateService
    {
        private static readonly object SyncRoot = new();
        private static readonly HashSet<int> SessionVisibleCategoryKeys = new();

        public static void RememberCreatedCategory(int categoryKey)
        {
            if (categoryKey <= 0) return;

            lock (SyncRoot)
            {
                SessionVisibleCategoryKeys.Add(categoryKey);
            }
        }

        public static void ForgetCategory(int categoryKey)
        {
            if (categoryKey <= 0) return;

            lock (SyncRoot)
            {
                SessionVisibleCategoryKeys.Remove(categoryKey);
            }
        }

        public static IReadOnlyList<int> GetSessionVisibleCategoryKeys()
        {
            lock (SyncRoot)
            {
                return new List<int>(SessionVisibleCategoryKeys);
            }
        }
    }
}
