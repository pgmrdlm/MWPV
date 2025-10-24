// File: CategoryItemModels.cs
// Scope: MODELS ONLY (no services, no DB, no encryption calls)
// Namespace per user: MWPV.Models

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MWPV.Models
{
    /// <summary>
    /// Lightweight record describing the DB row for a Category Item.
    /// NOTE: Column names/types should be mapped by your DAL later.
    /// This class exists only to give the UI a strongly-typed place
    /// to hang the JSON contract while we wire things up in chunks.
    /// </summary>
    public class CategoryItemDetail
    {
        // --- Scalar summaries for fast grid/filter (non-sensitive) ---
        public long CategoryItemKey { get; set; }          // map to your PK
        public long CategoryKey { get; set; }              // parent category FK
        public string? DisplayName { get; set; }           // item title/alias for grid

        // Optional projected summaries (cache of JSON primaries)
        public string? PrimaryAccountAlias { get; set; }   // non-sensitive alias/name
        public string? PrimaryAccountLast4 { get; set; }   // non-sensitive tail
        public string? PrimaryCardBrand { get; set; }      // e.g., Visa, MasterCard
        public string? PrimaryCardLast4 { get; set; }      // non-sensitive tail

        // --- JSON payloads (to be encrypted in persistence layer later) ---
        public string SecureDataJson { get; set; } = string.Empty; // root JSON payload (encrypted at rest later)
        public string? SecureMetaJson { get; set; }                 // optional meta blob if you keep it split
    }

    /// <summary>
    /// Root of the secure JSON payload stored with the Category Item.
    /// Sections are optional; empty lists mean "no data".
    /// </summary>
    public class SecureData
    {
        public List<SecurityQuestionItem> SecurityQuestions { get; set; } = new();
        public List<BankCardItem> BankCards { get; set; } = new();
        public List<AccountItem> Accounts { get; set; } = new();

        /// <summary>Free-form notes. Treat as sensitive if user chooses.</summary>
        public string? Notes { get; set; }

        /// <summary>Meta info for schema/versioning and timestamps.</summary>
        public SecureMeta Meta { get; set; } = new();
    }

    /// <summary>
    /// Metadata carried inside the secure JSON.
    /// </summary>
    public class SecureMeta
    {
        /// <summary>Schema version of the SecureData contract.</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>UTC timestamps for auditing (set by app code later).</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Optional application-specific flags.</summary>
        public string? AppTag { get; set; }
    }

    /// <summary>
    /// A single security question & answer pair.
    /// </summary>
    public class SecurityQuestionItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Question { get; set; } = string.Empty;

        // SENSITIVE: keep encrypted at rest; reveal-on-demand in UI.
        public string Answer { get; set; } = string.Empty;

        public bool IsPrimary { get; set; } = false;
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Bank/credit/debit card entry.
    /// </summary>
    public class BankCardItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>User-facing name for this card (non-sensitive).</summary>
        public string? Alias { get; set; }

        /// <summary>Brand printed on the card face (non-sensitive).</summary>
        public string? Brand { get; set; } // e.g., "Chase", "Citi", "Local Credit Union"

        /// <summary>Network (Visa/Mastercard/etc.). Serialized as string.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CardNetwork Network { get; set; } = CardNetwork.Unknown;

        public string? NameOnCard { get; set; }

        // Non-sensitive summaries used in grids
        public string? Last4 { get; set; }
        public int? ExpMonth { get; set; }   // 1..12
        public int? ExpYear { get; set; }    // yyyy

        public string? Issuer { get; set; }        // e.g., "Chase Bank"
        public string? SupportPhone { get; set; }  // e.g., toll-free number
        public string? WebUrl { get; set; }

        public bool IsPrimary { get; set; } = false;

        // SENSITIVE fields (encrypt at rest; never index/log)
        public string? Pan { get; set; }      // full card number
        public string? Cvv { get; set; }      // security code
        public string? TrackData { get; set; } // if ever used, treat as highly sensitive

        public string? Notes { get; set; }
    }

    /// <summary>
    /// Bank account, online account, or similar credentialed account.
    /// </summary>
    public class AccountItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Display alias, e.g., "Main Checking" or "Acme Portal".</summary>
        public string? Alias { get; set; }

        /// <summary>Institution or site name.</summary>
        public string? Institution { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AccountType Type { get; set; } = AccountType.Other;

        // Non-sensitive summaries for grid
        public string? AccountLast4 { get; set; }      // tail of acct #
        public string? RoutingLast4 { get; set; }      // tail of routing #
        public string? UsernameHint { get; set; }      // e.g., masked username
        public string? WebUrl { get; set; }
        public string? SupportPhone { get; set; }

        public bool IsPrimary { get; set; } = false;

        // SENSITIVE: encrypt at rest; reveal-on-demand; wipe on hide/close
        public string? AccountNumber { get; set; }
        public string? RoutingNumber { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }

        public string? Notes { get; set; }
    }

    /// <summary>
    /// Payment card networks.
    /// </summary>
    public enum CardNetwork
    {
        Unknown = 0,
        Visa,
        Mastercard,
        Amex,
        Discover,
        Diners,
        Jcb,
        UnionPay,
        PrivateLabel,
        Other
    }

    /// <summary>
    /// Common account types for filtering and templates.
    /// </summary>
    public enum AccountType
    {
        Other = 0,
        Checking,
        Savings,
        CreditCard,
        Loan,
        Investment,
        Retirement,
        Utility,
        Membership,
        Website
    }
}
