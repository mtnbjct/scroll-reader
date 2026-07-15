using ScrollReader.Native;
using ScrollReader.Segmentation;

namespace ScrollReader;

/// <summary>
/// One reading session: captures the selection, shows the overlay, and steps
/// through segments as the wheel turns. Wheel down advances, wheel up goes
/// back; scrolling past the end, Esc, a click, or any key press ends it.
/// </summary>
internal sealed class ReadingSession
{
    private IReadOnlyList<string> _segments = Array.Empty<string>();
    private OverlayWindow? _overlay;
    private MouseHook? _mouse;
    private KeyboardHook? _keyboard;
    private int _index;
    private int _wheelAccumulator;
    private bool _ended;

    public bool IsActive { get; private set; }

    public event Action? Ended;

    public void Start()
    {
        NativeMethods.GetCursorPos(out var pt);
        var cursor = new System.Drawing.Point(pt.X, pt.Y);

        var text = TextCapture.CaptureSelection();
        var segments = text is null ? Array.Empty<string>() : Segmenter.Segment(text);
        if (segments.Count == 0)
        {
            new OverlayWindow().ShowTransientMessage("テキストが選択されていません", cursor);
            Finish();
            return;
        }

        _segments = segments;
        IsActive = true;

        _overlay = new OverlayWindow();
        _overlay.ShowAt(cursor);
        _overlay.SetSegment(_segments[0], 0, _segments.Count);

        _mouse = new MouseHook();
        _mouse.Wheel += OnWheel;
        _mouse.ButtonDown += End;
        _mouse.Install();

        _keyboard = new KeyboardHook();
        _keyboard.EscapePressed += End;
        _keyboard.OtherKeyDown += End;
        _keyboard.Install();
    }

    private void OnWheel(int delta)
    {
        // Wheel down (negative delta) reads forward. Accumulate so that
        // high-resolution wheels/touchpads with sub-notch deltas work too.
        _wheelAccumulator += -delta;
        var steps = 0;
        while (_wheelAccumulator >= 120) { _wheelAccumulator -= 120; steps++; }
        while (_wheelAccumulator <= -120) { _wheelAccumulator += 120; steps--; }
        if (steps == 0) return;

        var next = _index + steps;
        if (next >= _segments.Count)
        {
            End();
            return;
        }
        _index = Math.Max(0, next);
        _overlay?.SetSegment(_segments[_index], _index, _segments.Count);
    }

    public void End()
    {
        if (_ended) return;
        _mouse?.Dispose();
        _mouse = null;
        _keyboard?.Dispose();
        _keyboard = null;
        _overlay?.Close();
        _overlay = null;
        Finish();
    }

    private void Finish()
    {
        if (_ended) return;
        _ended = true;
        IsActive = false;
        Ended?.Invoke();
    }
}
