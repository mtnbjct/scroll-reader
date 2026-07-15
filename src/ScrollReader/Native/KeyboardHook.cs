using System.Runtime.InteropServices;

namespace ScrollReader.Native;

/// <summary>
/// Low-level keyboard hook, installed only while a reading session is active.
/// Esc is swallowed and ends the session; any other key press ends the session
/// but is passed through to the focused app.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private IntPtr _hook;
    private NativeMethods.LowLevelProc? _proc;

    public event Action? EscapePressed;
    public event Action? OtherKeyDown;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = Callback;
        _hook = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandleW(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"WH_KEYBOARD_LL hook failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == NativeMethods.VK_ESCAPE)
            {
                EscapePressed?.Invoke();
                return 1;
            }
            // Ignore bare modifiers so the user can release Ctrl/Alt from the
            // activation hotkey without instantly ending the session.
            if (info.vkCode is not (NativeMethods.VK_SHIFT or NativeMethods.VK_CONTROL or NativeMethods.VK_MENU
                or NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
                && info.vkCode is not (>= 0xA0 and <= 0xA5)) // L/R shift, ctrl, alt
            {
                OtherKeyDown?.Invoke();
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
