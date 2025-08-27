using System;
using System.Windows.Controls;

namespace MWPV.Utilities.UI
{
    public static class UICleaner
    {
        public static void Clear(PasswordBox pb)
        {
            if (pb == null) return;

            try { pb.SecurePassword?.Clear(); } catch { /* ignore */ }

            // Best-effort overwrite of the managed Password before clearing
            try
            {
                int len = pb.Password?.Length ?? 0;
                if (len > 0)
                {
                    pb.Password = new string('\0', Math.Min(len, 256));
                }
            }
            catch { /* ignore */ }

            try { pb.Clear(); } catch { /* ignore */ }
        }

        public static void Clear(TextBox tb)
        {
            if (tb == null) return;

            int len = tb.Text?.Length ?? 0;
            if (len > 0)
            {
                try { tb.Text = new string('\0', Math.Min(len, 1024)); } catch { /* ignore */ }
            }

            try { tb.Clear(); } catch { /* ignore */ }
            try { tb.SelectionStart = 0; tb.SelectionLength = 0; } catch { /* ignore */ }
        }
    }
}
