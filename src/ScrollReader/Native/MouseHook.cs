using System.Runtime.InteropServices;

namespace ScrollReader.Native;

/// <summary>
/// Low-level mouse hook, installed only while a reading session is active.
/// Wheel events are swallowed (the app under the cursor must not scroll);
/// button presses are reported but passed through.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private IntPtr _hook;
    private NativeMethods.LowLevelProc? _proc;

    public event Action<int>? Wheel;
    public event Action? ButtonDown;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = Callback;
        _hook = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandleW(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"WH_MOUSE_LL hook failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            switch ((int)wParam)
            {
                case NativeMethods.WM_MOUSEWHEEL:
                    var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    Wheel?.Invoke((short)(info.mouseData >> 16));
                    return 1;
                case NativeMethods.WM_LBUTTONDOWN:
                case NativeMethods.WM_RBUTTONDOWN:
                case NativeMethods.WM_MBUTTONDOWN:
                    ButtonDown?.Invoke();
                    break;
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
    }
}
