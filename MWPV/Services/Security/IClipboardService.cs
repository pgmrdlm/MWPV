namespace MWPV.Services.Security
{
    public interface IClipboardService
    {
        bool CopySensitiveText(string value, string reasonCode);
        void ClearIfOwned();
        void ClearNow();
    }
}
