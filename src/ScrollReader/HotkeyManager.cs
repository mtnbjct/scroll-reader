using System.Windows.Interop;
using ScrollReader.Native;

namespace ScrollReader;

/// <summary>Registers the global activation hotkey on a message-only window.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB00C;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("ScrollReaderMessageWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = HwndMessage,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers the hotkey, replacing any previous registration.</summary>
    public bool Register(uint modifiers, uint vk)
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, vk);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && (int)wParam == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.Dispose();
    }
}
