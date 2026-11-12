using System.Runtime.InteropServices;
using static Snowfall.Native;

namespace Snowfall;

/// <summary>
/// Global low-level mouse hook that only observes left-button presses (never swallows them),
/// so clicking a snow pile can crumble it while the click still reaches whatever is underneath.
/// Runs on the UI thread that installs it; the callback must stay fast.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private readonly LowLevelProc _proc;
    private readonly Action<int, int> _onLeftDown;
    private IntPtr _hook;

    public MouseHook(Action<int, int> onLeftDown)
    {
        _onLeftDown = onLeftDown;
        _proc = Proc; // keep the delegate alive for the lifetime of the hook
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == WM_LBUTTONDOWN)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            try { _onLeftDown(info.pt.X, info.pt.Y); }
            catch { /* never break the system mouse chain */ }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
