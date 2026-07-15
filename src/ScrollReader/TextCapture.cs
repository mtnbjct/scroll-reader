using System.Text;
using System.Windows.Automation;
using ScrollReader.Native;

namespace ScrollReader;

/// <summary>
/// Captures the text currently selected in the foreground app.
/// UI Automation first; falls back to sending Ctrl+C and reading the
/// clipboard (restoring the previous text content afterwards).
/// </summary>
internal static class TextCapture
{
    public static string? CaptureSelection()
    {
        var text = TryUiAutomation();
        if (string.IsNullOrWhiteSpace(text)) text = TryClipboard();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? TryUiAutomation()
    {
        try
        {
            // UIA calls can block on unresponsive providers; bound the wait.
            var task = Task.Run(static () =>
            {
                var focused = AutomationElement.FocusedElement;
                if (focused is null) return null;
                if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
                var ranges = ((TextPattern)pattern).GetSelection();
                if (ranges is null || ranges.Length == 0) return null;
                var sb = new StringBuilder();
                foreach (var range in ranges) sb.Append(range.GetText(-1));
                return sb.ToString();
            });
            return task.Wait(TimeSpan.FromMilliseconds(800)) ? task.Result : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryClipboard()
    {
        string? backup = null;
        try
        {
            if (System.Windows.Clipboard.ContainsText()) backup = System.Windows.Clipboard.GetText();
        }
        catch
        {
            // Clipboard busy; proceed without a backup.
        }

        var seqBefore = NativeMethods.GetClipboardSequenceNumber();
        SendCtrlC();

        string? captured = null;
        var changed = false;
        for (var i = 0; i < 40; i++)
        {
            Thread.Sleep(10);
            if (NativeMethods.GetClipboardSequenceNumber() == seqBefore) continue;
            changed = true;
            // The source app may still be writing formats; retry briefly.
            for (var j = 0; j < 5 && captured is null; j++)
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText()) captured = System.Windows.Clipboard.GetText();
                    break;
                }
                catch
                {
                    Thread.Sleep(20);
                }
            }
            break;
        }

        if (changed && backup is not null)
        {
            try { System.Windows.Clipboard.SetText(backup); }
            catch { /* best effort */ }
        }
        return captured;
    }

    private static void SendCtrlC()
    {
        // The user may still be holding the activation hotkey's modifiers;
        // release them all before injecting Ctrl+C so the target app sees a
        // clean copy chord.
        var inputs = new[]
        {
            NativeMethods.KeyInput(NativeMethods.VK_MENU, up: true),
            NativeMethods.KeyInput(NativeMethods.VK_SHIFT, up: true),
            NativeMethods.KeyInput(NativeMethods.VK_LWIN, up: true),
            NativeMethods.KeyInput(NativeMethods.VK_RWIN, up: true),
            NativeMethods.KeyInput(NativeMethods.VK_CONTROL, up: true),
            NativeMethods.KeyInput(NativeMethods.VK_CONTROL, up: false),
            NativeMethods.KeyInput(NativeMethods.VK_C, up: false),
            NativeMethods.KeyInput(NativeMethods.VK_C, up: true),
            NativeMethods.KeyInput(NativeMethods.VK_CONTROL, up: true),
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
