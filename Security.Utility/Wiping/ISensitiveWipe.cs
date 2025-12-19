// File: Security.Utility/Wiping/ISensitiveWipe.cs
namespace Security.Utility.Wiping.Contracts
{
    /// <summary>
    /// Implemented by any model/row object that holds sensitive data and can explicitly wipe it.
    /// Wipe() should be safe to call multiple times (idempotent).
    /// </summary>
    public interface ISensitiveWipe
    {
        /// <summary>
        /// Overwrite/zeroize any sensitive fields on this instance (strings, char[], byte[], etc).
        /// After this call, the instance should no longer hold recoverable sensitive values.
        /// </summary>
        void Wipe();
    }
}
