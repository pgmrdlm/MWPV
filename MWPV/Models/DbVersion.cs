namespace MWPV.Models
{
    /// <summary>
    /// Represents a single database version row from the DbVersion table.
    /// </summary>
    public sealed class DbVersion
    {
        /// <summary>
        /// Primary key (Id).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Database version label (Version).
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Current-version flag (IsCurrent).
        /// </summary>
        public bool IsCurrent { get; set; }

        /// <summary>
        /// Row creation / applied timestamp exposed to the app as CreatedAt.
        /// Backed by DbVersion.AppliedOn in the current schema.
        /// </summary>
        public string CreatedAt { get; set; } = string.Empty;
    }
}
