using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ScrollReader.Native;

/// <summary>
/// Permanent low-level mouse hook used when the activation hotkey is a
/// middle-click chord. A matching click (exact modifier match) is swallowed —
/// along with its button-up — and Triggered is raised via the dispatcher,
/// because a hook callback must return quickly.
/// </summary>
internal sealed class MiddleClickActivator : IDisposable
{
    private readonly uint _modifiers;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private IntPtr _hook;
    private NativeMethods.LowLevelProc? _proc;
    private bool _swallowNextUp;

    public event Action? Triggered;

    public MiddleClickActivator(uint modifiers) => _modifiers = modifiers;

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
                case NativeMethods.WM_MBUTTONDOWN when ModifiersMatch():
                    _swallowNextUp = true;
                    _dispatcher.BeginInvoke(() => Triggered?.Invoke());
                    return 1;
                case NativeMethods.WM_MBUTTONUP when _swallowNextUp:
                    _swallowNextUp = false;
                    return 1;
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Exact match: required modifiers held, others not.</summary>
    private bool ModifiersMatch() =>
        Required(NativeMethods.MOD_CONTROL) == Held(NativeMethods.VK_CONTROL)
        && Required(NativeMethods.MOD_ALT) == Held(NativeMethods.VK_MENU)
        && Required(NativeMethods.MOD_SHIFT) == Held(NativeMethods.VK_SHIFT)
        && Required(NativeMethods.MOD_WIN) == (Held(NativeMethods.VK_LWIN) || Held(NativeMethods.VK_RWIN));

    private bool Required(uint modifier) => (_modifiers & modifier) != 0;

    private static bool Held(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

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
