using System;

namespace MWPV.Models
{
    /// <summary>
    /// Maps to table: ComboDetail
    /// Columns (per schema): ComboDet, ComboTyp, Seq, Code, Description, Active, CreatedUtc, UpdatedUtc
    /// </summary>
    public sealed class ComboDetail
    {
        /// <summary>Primary key.</summary>
        public int ComboDet { get; set; }

        /// <summary>FK to ComboType.ComboType.</summary>
        public int ComboTyp { get; set; }

        /// <summary>Display order within a type.</summary>
        public int Seq { get; set; }

        /// <summary>Short code (e.g., "LOGIN", "SMOKE").</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Human friendly label.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>0/1 active flag.</summary>
        public int Active { get; set; }

        /// <summary>ISO-8601 UTC timestamp the row was created.</summary>
        public string CreatedUtc { get; set; } = string.Empty;

        /// <summary>ISO-8601 UTC timestamp the row was last updated.</summary>
        public string UpdatedUtc { get; set; } = string.Empty;

        /// <summary>Convenience: true if Active != 0.</summary>
        public bool IsActive => Active != 0;

        /// <summary>
        /// Convenience for bindings: prefer Description, fall back to Code.
        /// </summary>
        public string Display => string.IsNullOrWhiteSpace(Description) ? Code : Description;

        public override string ToString() => Display;
    }
}
