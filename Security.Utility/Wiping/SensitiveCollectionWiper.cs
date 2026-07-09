// File: Security.Utility/Wiping/SensitiveCollectionWiper.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Security.Utility.Wiping
{
    using Security.Utility.Wiping.Contracts;

    /// <summary>
    /// Common helper for wiping "sub-grid" collections (rows) and clearing the list.
    /// Goal: make the wipe call obvious, centralized, repeatable, and easy to verify.
    /// </summary>
    public static class SensitiveCollectionWiper
    {
        /// <summary>
        /// Wipes every row (calls ISensitiveWipe.Wipe()) then clears the collection.
        /// Returns counts so callers can log/verify behavior.
        /// </summary>
        public static WipeResult WipeAndClear<T>(
            IList<T>? rows,
            Action<string>? debugLog = null)
            where T : class, ISensitiveWipe
        {
            if (rows is null)
                return WipeResult.Empty("rows=null");

            // Snapshot first in case bindings/events mutate the list while we iterate.
            List<T> snapshot = rows.Count == 0 ? new List<T>() : rows.ToList();

            int attempted = snapshot.Count;
            int wipedOk = 0;
            int wipeFailed = 0;

            debugLog?.Invoke($"[ROW-WIPE] begin: rows={attempted}");

            foreach (var row in snapshot)
            {
                if (row is null) continue;

                try
                {
                    row.Wipe();
                    wipedOk++;
                }
                catch (Exception ex)
                {
                    wipeFailed++;
                    debugLog?.Invoke($"[ROW-WIPE] row wipe FAILED: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Clear original list last (important for the UI grid)
            try
            {
                rows.Clear();
                debugLog?.Invoke($"[ROW-WIPE] cleared list (now {rows.Count})");
            }
            catch (Exception ex)
            {
                debugLog?.Invoke($"[ROW-WIPE] list clear FAILED: {ex.GetType().Name}: {ex.Message}");
            }

            debugLog?.Invoke($"[ROW-WIPE] end: attempted={attempted} ok={wipedOk} failed={wipeFailed}");
            return new WipeResult(attempted, wipedOk, wipeFailed, null);
        }

        /// <summary>
        /// Wipes every row and clears the collection, returning only a Security.Utility
        /// technical result. No message text, exception text, sensitive values, or caller
        /// actions are returned.
        /// </summary>
        public static SecurityUtilityResult WipeAndClearResult<T>(IList<T>? rows)
            where T : class, ISensitiveWipe
        {
            var wipeResult = WipeAndClear(rows, debugLog: null);
            return wipeResult.AllOk
                ? Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success)
                : Result(SecurityUtilityReturnCode.SecureDeleteFailed, SecurityUtilityResultKind.Failure);
        }

        /// <summary>
        /// Same as WipeAndClear, but works with any IEnumerable by wiping the items
        /// and optionally calling a provided clear action (ObservableCollection, etc.).
        /// </summary>
        public static WipeResult WipeAndClear<T>(
            IEnumerable<T>? rows,
            Action? clearAction,
            Action<string>? debugLog = null)
            where T : class, ISensitiveWipe
        {
            if (rows is null)
                return WipeResult.Empty("rows=null");

            var snapshot = rows as IList<T> ?? rows.ToList();

            int attempted = snapshot.Count;
            int wipedOk = 0;
            int wipeFailed = 0;

            debugLog?.Invoke($"[ROW-WIPE] begin: rows={attempted}");

            foreach (var row in snapshot)
            {
                if (row is null) continue;

                try
                {
                    row.Wipe();
                    wipedOk++;
                }
                catch (Exception ex)
                {
                    wipeFailed++;
                    debugLog?.Invoke($"[ROW-WIPE] row wipe FAILED: {ex.GetType().Name}: {ex.Message}");
                }
            }

            try
            {
                clearAction?.Invoke();
                debugLog?.Invoke("[ROW-WIPE] clearAction invoked");
            }
            catch (Exception ex)
            {
                debugLog?.Invoke($"[ROW-WIPE] clearAction FAILED: {ex.GetType().Name}: {ex.Message}");
            }

            debugLog?.Invoke($"[ROW-WIPE] end: attempted={attempted} ok={wipedOk} failed={wipeFailed}");
            return new WipeResult(attempted, wipedOk, wipeFailed, null);
        }

        /// <summary>
        /// Wipes rows and invokes the supplied clear action, returning only a Security.Utility
        /// technical result. No message text, exception text, sensitive values, or caller
        /// actions are returned.
        /// </summary>
        public static SecurityUtilityResult WipeAndClearResult<T>(
            IEnumerable<T>? rows,
            Action? clearAction)
            where T : class, ISensitiveWipe
        {
            var wipeResult = WipeAndClear(rows, clearAction, debugLog: null);
            return wipeResult.AllOk
                ? Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success)
                : Result(SecurityUtilityReturnCode.SecureDeleteFailed, SecurityUtilityResultKind.Failure);
        }

        private static SecurityUtilityResult Result(
            SecurityUtilityReturnCode code,
            SecurityUtilityResultKind kind)
            => new()
            {
                Code = code,
                Kind = kind
            };
    }

    /// <summary>
    /// Simple result record to support verification/logging.
    /// </summary>
    public readonly record struct WipeResult(
        int Attempted,
        int WipedOk,
        int WipeFailed,
        string? Note)
    {
        public static WipeResult Empty(string note) => new(0, 0, 0, note);
        public bool AllOk => Attempted == WipedOk && WipeFailed == 0;
    }
}
