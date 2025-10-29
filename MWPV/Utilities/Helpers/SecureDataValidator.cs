// File: Utilities/Helpers/SecureDataValidator.cs
// Scope: Validation helpers for SecureData (no DB, no encryption)
// Depends on: MWPV.Models

using System;
using System.Linq;
using MWPV.Models;

namespace Utilities.Helpers
{
    internal static class SecureDataValidator
    {
        /// <summary>
        /// Normalize trivial fields (trim strings, fix Last4 masks) and
        /// enforce exactly one primary per non-empty section.
        /// Returns the same instance for chaining.
        /// </summary>
        public static SecureData NormalizeAndValidate(this SecureData data)
        {
            data ??= new SecureData();

            // --- Security Questions ---
            NormalizeList(data.SecurityQuestions, q =>
            {
                q.Id = SafeId(q.Id);
                q.Question = q.Question?.Trim() ?? string.Empty;
                q.Answer = q.Answer?.Trim() ?? string.Empty; // still sensitive
                q.Notes = TrimOrNull(q.Notes);
            });
            EnsureSinglePrimary(data.SecurityQuestions, item => item.IsPrimary);

            // --- Bank Cards ---
            NormalizeList(data.BankCards, c =>
            {
                c.Id = SafeId(c.Id);
                c.Alias = TrimOrNull(c.Alias);
                c.Brand = TrimOrNull(c.Brand);
                c.NameOnCard = TrimOrNull(c.NameOnCard);
                c.Last4 = Last4OrNull(c.Last4);
                c.Issuer = TrimOrNull(c.Issuer);
                c.SupportPhone = TrimOrNull(c.SupportPhone);
                c.WebUrl = TrimOrNull(c.WebUrl);
                c.Notes = TrimOrNull(c.Notes);

                if (c.ExpMonth is < 1 or > 12) c.ExpMonth = null;
                if (c.ExpYear is < 1900 or > 9999) c.ExpYear = null;
            });
            EnsureSinglePrimary(data.BankCards, item => item.IsPrimary);

            // --- Accounts ---
            NormalizeList(data.Accounts, a =>
            {
                a.Id = SafeId(a.Id);
                a.Alias = TrimOrNull(a.Alias);
                a.Institution = TrimOrNull(a.Institution);
                a.AccountLast4 = Last4OrNull(a.AccountLast4);
                a.RoutingLast4 = Last4OrNull(a.RoutingLast4);
                a.UsernameHint = TrimOrNull(a.UsernameHint);
                a.WebUrl = TrimOrNull(a.WebUrl);
                a.SupportPhone = TrimOrNull(a.SupportPhone);
                a.Notes = TrimOrNull(a.Notes);
            });
            EnsureSinglePrimary(data.Accounts, item => item.IsPrimary);

            // Meta upkeep (leave CreatedUtc alone if already set)
            data.Meta ??= new SecureMeta();
            if (data.Meta.CreatedUtc == default) data.Meta.CreatedUtc = DateTime.UtcNow;
            data.Meta.ModifiedUtc = DateTime.UtcNow;

            return data;
        }

        /// <summary>Lightweight validity check for display/save decisions.</summary>
        public static bool IsLikelyValid(this SecureData data)
        {
            if (data is null) return false;

            // Expiration sanity: if either month or year is set, both should be set
            bool cardExpOk = data.BankCards.All(c =>
                (c.ExpMonth is null && c.ExpYear is null) ||
                (c.ExpMonth is >= 1 and <= 12 && c.ExpYear is >= 1900 and <= 9999));

            // Single-primary invariant per section
            bool primariesOk =
                HasSinglePrimaryOrNone(data.SecurityQuestions.Select(x => x.IsPrimary)) &&
                HasSinglePrimaryOrNone(data.BankCards.Select(x => x.IsPrimary)) &&
                HasSinglePrimaryOrNone(data.Accounts.Select(x => x.IsPrimary));

            return cardExpOk && primariesOk;
        }

        // ----------------- helpers -----------------

        private static void NormalizeList<T>(System.Collections.Generic.IList<T> list, Action<T> norm)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item is null) { list.RemoveAt(i--); continue; }
                norm(item);
            }
        }

        private static void EnsureSinglePrimary<T>(System.Collections.Generic.IList<T> list, Func<T, bool> isPrimary)
        {
            if (list == null || list.Count == 0) return;

            int primaryCount = list.Count(isPrimary);
            if (primaryCount <= 1) return;

            // Keep the first primary, unset the rest
            bool keep = true;
            foreach (var item in list)
            {
                if (!isPrimary(item)) continue;

                if (keep) { keep = false; }
                else
                {
                    // reflectively set IsPrimary = false (all our types have that property)
                    var prop = item!.GetType().GetProperty("IsPrimary");
                    prop?.SetValue(item, false);
                }
            }
        }

        private static bool HasSinglePrimaryOrNone(System.Collections.Generic.IEnumerable<bool> flags)
        {
            int count = 0;
            foreach (var f in flags) if (f) count++;
            return count <= 1;
        }

        private static string? TrimOrNull(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string SafeId(string? id)
            => string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();

        private static string? Last4OrNull(string? s)
        {
            s = TrimOrNull(s);
            if (s == null) return null;
            // Keep last 4 digits if user pasted a full number by mistake
            var digits = new string(s.Where(char.IsDigit).ToArray());
            if (digits.Length >= 4) return digits[^4..];
            return null;
        }
    }
}
