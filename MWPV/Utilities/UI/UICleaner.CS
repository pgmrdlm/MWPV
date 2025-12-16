using System;
using System.Diagnostics;
using System.Windows.Controls;

namespace MWPV.Utilities.UI
{
    public static class UICleaner
    {
        // Keep debug output safe: lengths only, never values.
        private static void DebugLen(string tag, int beforeLen, int afterLen)
        {
#if DEBUG
            Debug.WriteLine($"[UICLEANER][{tag}] len {beforeLen} -> {afterLen}");
#endif
        }

        public static void Clear(PasswordBox pb)
        {
            if (pb == null) return;

            int beforeLen = 0;
            try { beforeLen = pb.Password?.Length ?? 0; } catch { /* ignore */ }

#if DEBUG
            Debug.WriteLine($"[UICLEANER][PasswordBox] Clear() start (len={beforeLen})");
#endif

            try
            {
                pb.SecurePassword?.Clear();
#if DEBUG
                Debug.WriteLine("[UICLEANER][PasswordBox] SecurePassword.Clear() called");
#endif
            }
            catch
            {
#if DEBUG
                Debug.WriteLine("[UICLEANER][PasswordBox] SecurePassword.Clear() failed/ignored");
#endif
            }

            // Best-effort overwrite of the managed Password before clearing
            try
            {
                int len = beforeLen;
                if (len > 0)
                {
                    pb.Password = new string('\0', Math.Min(len, 256));
#if DEBUG
                    Debug.WriteLine($"[UICLEANER][PasswordBox] Overwrite attempted (requestedLen={len}, wroteLen={Math.Min(len, 256)})");
#endif
                }
                else
                {
#if DEBUG
                    Debug.WriteLine("[UICLEANER][PasswordBox] Overwrite skipped (len=0)");
#endif
                }
            }
            catch
            {
#if DEBUG
                Debug.WriteLine("[UICLEANER][PasswordBox] Overwrite failed/ignored");
#endif
            }

            try
            {
                pb.Clear();
#if DEBUG
                Debug.WriteLine("[UICLEANER][PasswordBox] pb.Clear() called");
#endif
            }
            catch
            {
#if DEBUG
                Debug.WriteLine("[UICLEANER][PasswordBox] pb.Clear() failed/ignored");
#endif
            }

            int afterLen = 0;
            try { afterLen = pb.Password?.Length ?? 0; } catch { /* ignore */ }

            DebugLen("PasswordBox", beforeLen, afterLen);

#if DEBUG
            Debug.WriteLine("[UICLEANER][PasswordBox] Clear() end");
#endif
        }

        public static void Clear(TextBox tb)
        {
            if (tb == null) return;

            int beforeLen = 0;
            try { beforeLen = tb.Text?.Length ?? 0; } catch { /* ignore */ }

#if DEBUG
            Debug.WriteLine($"[UICLEANER][TextBox] Clear() start (len={beforeLen})");
#endif

            if (beforeLen > 0)
            {
                try
                {
                    int wrote = Math.Min(beforeLen, 1024);
                    tb.Text = new string('\0', wrote);
#if DEBUG
                    Debug.WriteLine($"[UICLEANER][TextBox] Overwrite attempted (requestedLen={beforeLen}, wroteLen={wrote})");
#endif
                }
                catch
                {
#if DEBUG
                    Debug.WriteLine("[UICLEANER][TextBox] Overwrite failed/ignored");
#endif
                }
            }
            else
            {
#if DEBUG
                Debug.WriteLine("[UICLEANER][TextBox] Overwrite skipped (len=0)");
#endif
            }

            try
            {
                tb.Clear();
#if DEBUG
                Debug.WriteLine("[UICLEANER][TextBox] tb.Clear() called");
#endif
            }
            catch
            {
#if DEBUG
                Debug.WriteLine("[UICLEANER][TextBox] tb.Clear() failed/ignored");
#endif
            }

            try
            {
                tb.SelectionStart = 0;
                tb.SelectionLength = 0;
#if DEBUG
                Debug.WriteLine("[UICLEANER][TextBox] Selection reset called");
#endif
            }
            catch
            {
#if DEBUG
                Debug.WriteLine("[UICLEANER][TextBox] Selection reset failed/ignored");
#endif
            }

            int afterLen = 0;
            try { afterLen = tb.Text?.Length ?? 0; } catch { /* ignore */ }

            DebugLen("TextBox", beforeLen, afterLen);

#if DEBUG
            Debug.WriteLine("[UICLEANER][TextBox] Clear() end");
#endif
        }
    }
}
