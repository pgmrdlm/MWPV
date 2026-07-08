using System;
using System.Windows.Controls;

namespace MWPV.Utilities.UI
{
    public static class UICleaner
    {
        public static void Clear(PasswordBox pb)
        {
            if (pb == null) return;

            int beforeLen = 0;
            try { beforeLen = pb.Password?.Length ?? 0; } catch { /* ignore */ }

            try
            {
                pb.SecurePassword?.Clear();
            }
            catch
            {
            }

            // Best-effort overwrite of the managed Password before clearing
            try
            {
                int len = beforeLen;
                if (len > 0)
                {
                    pb.Password = new string('\0', Math.Min(len, 256));
                }
            }
            catch
            {
            }

            try
            {
                pb.Clear();
            }
            catch
            {
            }

            int afterLen = 0;
            try { afterLen = pb.Password?.Length ?? 0; } catch { /* ignore */ }

            _ = afterLen;
        }

        public static void Clear(TextBox tb)
        {
            if (tb == null) return;

            int beforeLen = 0;
            try { beforeLen = tb.Text?.Length ?? 0; } catch { /* ignore */ }

            if (beforeLen > 0)
            {
                try
                {
                    int wrote = Math.Min(beforeLen, 1024);
                    tb.Text = new string('\0', wrote);
                }
                catch
                {
                }
            }

            try
            {
                tb.Clear();
            }
            catch
            {
            }

            try
            {
                tb.SelectionStart = 0;
                tb.SelectionLength = 0;
            }
            catch
            {
            }

            int afterLen = 0;
            try { afterLen = tb.Text?.Length ?? 0; } catch { /* ignore */ }

            _ = afterLen;
        }
    }
}
