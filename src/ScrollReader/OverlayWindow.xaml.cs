using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScrollReader.Native;

namespace ScrollReader;

/// <summary>
/// Topmost, click-through, non-activating window that shows one segment at a
/// time near where the cursor was when the reading session started.
/// </summary>
public partial class OverlayWindow : Window
{
    private static readonly System.Windows.Media.Brush CurrentBrush = Frozen(0xF2, 0xF3, 0xF5);
    private static readonly System.Windows.Media.Brush RevisitBrush = Frozen(0x84, 0x8B, 0x94);

    private System.Drawing.Point _anchor;
    private double _progress;

    private static System.Windows.Media.SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => { Reposition(); UpdateProgressFill(); };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>Shows the overlay anchored above the given point (screen pixels).</summary>
    public void ShowAt(System.Drawing.Point cursorPx)
    {
        _anchor = cursorPx;
        Show();
        Reposition();
    }

    public void SetFontSize(double size) => SegmentText.FontSize = size;

    public void SetSegment(string text, int index, int total, bool revisit)
    {
        SegmentText.Text = text;
        SegmentText.Foreground = revisit ? RevisitBrush : CurrentBrush;
        ProgressText.Text = index == total - 1
            ? $"{total} / {total} ・ もう一度下で終了"
            : $"{index + 1} / {total}";
        _progress = total > 0 ? (index + 1) / (double)total : 0;
        UpdateProgressFill();
    }

    /// <summary>Shows a short informational message that closes itself.</summary>
    public void ShowTransientMessage(string message, System.Drawing.Point cursorPx)
    {
        SegmentText.FontSize = 18;
        SegmentText.Text = message;
        ProgressPanel.Visibility = Visibility.Collapsed;
        ProgressText.Visibility = Visibility.Collapsed;
        ShowAt(cursorPx);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }

    private void UpdateProgressFill()
    {
        ProgressFill.Width = Math.Max(0, _progress * ProgressTrack.ActualWidth);
    }

    private void Reposition()
    {
        if (!IsVisible) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var widthPx = ActualWidth * dpi.DpiScaleX;
        var heightPx = ActualHeight * dpi.DpiScaleY;
        var area = System.Windows.Forms.Screen.FromPoint(_anchor).WorkingArea;

        var leftPx = _anchor.X - widthPx / 2;
        leftPx = Math.Clamp(leftPx, area.Left, Math.Max(area.Left, area.Right - widthPx));

        // Prefer above the cursor; fall below when there is no room.
        var topPx = _anchor.Y - heightPx - 28;
        if (topPx < area.Top) topPx = Math.Min(_anchor.Y + 28, area.Bottom - heightPx);

        Left = leftPx / dpi.DpiScaleX;
        Top = topPx / dpi.DpiScaleY;
    }
}
