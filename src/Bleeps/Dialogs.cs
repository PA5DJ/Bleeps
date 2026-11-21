using System.Runtime.InteropServices;

namespace Bleeps;

/// <summary>
/// A message box centred over its owner window (v1.0.27, PA5DJ: "make sure windows are shown above the middle of the app
/// window"). Windows itself puts a MessageBox in the middle of the SCREEN; with Bleeps on one side of a wide screen, or on a
/// second screen, the box opened far away from it. A CBT hook on this thread catches the box the moment it is activated and
/// moves it over the owner (kept on the owner's screen). Forms of our own use <c>FormStartPosition.CenterParent</c>.
/// </summary>
internal static class Dialogs
{
    public static DialogResult Show(IWin32Window owner, string text, string caption,
                                    MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
    {
        var ownerHandle = owner.Handle;
        IntPtr hook = IntPtr.Zero;
        HookProc proc = (code, wParam, lParam) =>
        {
            if (code == 5 /* HCBT_ACTIVATE */)
            {
                try { CenterOver(wParam, ownerHandle); } catch { }
                if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }   // only the first: the box itself
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        };
        hook = SetWindowsHookEx(5 /* WH_CBT */, proc, IntPtr.Zero, GetCurrentThreadId());
        try { return MessageBox.Show(owner, text, caption, buttons, icon); }
        finally
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            GC.KeepAlive(proc);
        }
    }

    private static void CenterOver(IntPtr box, IntPtr owner)
    {
        if (!GetWindowRect(box, out var b) || !GetWindowRect(owner, out var o)) return;
        int w = b.Right - b.Left, h = b.Bottom - b.Top;
        var area = Screen.FromHandle(owner).WorkingArea;
        int x = Math.Clamp(o.Left + (o.Right - o.Left - w) / 2, area.Left, Math.Max(area.Left, area.Right - w));
        int y = Math.Clamp(o.Top + (o.Bottom - o.Top - h) / 2, area.Top, Math.Max(area.Top, area.Bottom - h));
        SetWindowPos(box, IntPtr.Zero, x, y, 0, 0, 0x0001 /* NOSIZE */ | 0x0004 /* NOZORDER */ | 0x0010 /* NOACTIVATE */);
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32")] private static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("user32")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
