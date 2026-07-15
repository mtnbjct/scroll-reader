using System.Diagnostics;
using System.Windows;
using ScrollReader.Native;
using WinForms = System.Windows.Forms;

namespace ScrollReader;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _startMenuItem;
    private HotkeyManager? _hotkey;
    private MiddleClickActivator? _middleClickActivator;
    private bool _middleClickActivation;
    private SettingsStore? _settings;
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

        _settings = new SettingsStore();
        _settings.Initialize();

        SetupTrayIcon();

        _hotkey = new HotkeyManager();
        _hotkey.HotkeyPressed += ToggleSession;
        ApplyHotkey();

        _settings.Changed += () =>
        {
            ApplyHotkey();
            _trayIcon?.ShowBalloonTip(2000, "Scroll Reader", "設定を再読み込みしました。", WinForms.ToolTipIcon.Info);
        };
    }

    private void ApplyHotkey()
    {
        var spec = _settings!.Current.Hotkey;
        if (!HotkeyParser.TryParse(spec, out var hotkey))
        {
            _trayIcon?.ShowBalloonTip(3000, "Scroll Reader",
                $"ホットキー \"{spec}\" を解釈できません。Ctrl+Alt+R を使用します。", WinForms.ToolTipIcon.Warning);
            spec = "Ctrl+Alt+R";
            HotkeyParser.TryParse(spec, out hotkey);
        }

        _middleClickActivator?.Dispose();
        _middleClickActivator = null;
        _hotkey!.Unregister();
        _middleClickActivation = hotkey.IsMiddleClick;

        if (hotkey.IsMiddleClick)
        {
            _middleClickActivator = new MiddleClickActivator(hotkey.Modifiers);
            _middleClickActivator.Triggered += ToggleSession;
            _middleClickActivator.Install();
        }
        else if (!_hotkey.Register(hotkey.Modifiers, hotkey.Vk))
        {
            _trayIcon?.ShowBalloonTip(3000, "Scroll Reader",
                $"ホットキー {spec} を登録できませんでした。他のアプリが使用中の可能性があります。",
                WinForms.ToolTipIcon.Warning);
        }

        if (_startMenuItem is not null) _startMenuItem.Text = $"読書モード開始 ({spec})";
        if (_trayIcon is not null) _trayIcon.Text = $"Scroll Reader — テキストを選択して {spec}";
    }

    private void SetupTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        _startMenuItem = new WinForms.ToolStripMenuItem("読書モード開始", null, (_, _) => ToggleSession());
        menu.Items.Add(_startMenuItem);
        menu.Items.Add("設定を開く", null, (_, _) => OpenSettingsFile());
        menu.Items.Add("設定フォルダを開く", null, (_, _) => OpenSettingsFolder());
        var autoStartItem = new WinForms.ToolStripMenuItem("サインイン時に自動起動")
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled(),
        };
        autoStartItem.CheckedChanged += (_, _) => AutoStart.SetEnabled(autoStartItem.Checked);
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitApp());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Scroll Reader",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ToggleSession();
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe &&
                System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon)
            {
                return icon;
            }
        }
        catch
        {
            // fall through to the generic icon
        }
        return System.Drawing.SystemIcons.Application;
    }

    private void OpenSettingsFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_settings!.FilePath) { UseShellExecute = true });
        }
        catch
        {
            Process.Start("explorer.exe", $"/select,\"{_settings!.FilePath}\"");
        }
    }

    private void OpenSettingsFolder()
    {
        // Explorer /select opens the (hidden) AppData folder and highlights
        // the file regardless of the "show hidden items" setting.
        Process.Start("explorer.exe", $"/select,\"{_settings!.FilePath}\"");
    }

    private void ToggleSession()
    {
        if (_session is { IsActive: true })
        {
            _session.End();
            return;
        }

        var settings = _settings!.Current;
        var foreground = GetForegroundProcessName();
        if (foreground is not null && settings.IsBlocked(foreground))
        {
            NativeMethods.GetCursorPos(out var pt);
            new OverlayWindow().ShowTransientMessage(
                $"{foreground} では無効になっています", new System.Drawing.Point(pt.X, pt.Y));
            return;
        }

        _session = new ReadingSession(settings, _middleClickActivation);
        _session.Ended += () => _session = null;
        _session.Start();
    }

    private static string? GetForegroundProcessName()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private void ExitApp()
    {
        _session?.End();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _middleClickActivator?.Dispose();
        _hotkey?.Dispose();
        _settings?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
