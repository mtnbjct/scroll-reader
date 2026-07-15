using System.Windows;
using ScrollReader.Native;
using WinForms = System.Windows.Forms;

namespace ScrollReader;

public partial class App : System.Windows.Application
{
    private const uint VK_R = 0x52;

    private Mutex? _instanceMutex;
    private WinForms.NotifyIcon? _trayIcon;
    private HotkeyManager? _hotkey;
    private ReadingSession? _session;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, "ScrollReader.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        SetupTrayIcon();

        _hotkey = new HotkeyManager();
        _hotkey.HotkeyPressed += ToggleSession;
        if (!_hotkey.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, VK_R))
        {
            _trayIcon?.ShowBalloonTip(3000, "Scroll Reader",
                "ホットキー Ctrl+Alt+R を登録できませんでした。他のアプリが使用中の可能性があります。",
                WinForms.ToolTipIcon.Warning);
        }
    }

    private void SetupTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("読書モード開始 (Ctrl+Alt+R)", null, (_, _) => ToggleSession());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitApp());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Scroll Reader — テキストを選択して Ctrl+Alt+R",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ToggleSession();
    }

    private void ToggleSession()
    {
        if (_session is { IsActive: true })
        {
            _session.End();
            return;
        }
        _session = new ReadingSession();
        _session.Ended += () => _session = null;
        _session.Start();
    }

    private void ExitApp()
    {
        _session?.End();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _hotkey?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
