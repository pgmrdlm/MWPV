namespace Utilities.Security
{
    /// <summary>
    /// Standardized reasons captured when recording early (pre-DB) login failures.
    /// Keep values stable; they may be stored in logs.
    /// </summary>
    public enum EarlyFailType
    {
        Unknown = 0,
        InvalidPassword = 1,
        InvalidKeyFile = 2,
        InvalidKeyPassword = 3
    }
}
