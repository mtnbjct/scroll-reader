using System.Windows.Threading;
using ScrollReader.Native;
using ScrollReader.Segmentation;

namespace ScrollReader;

/// <summary>
/// One reading session: captures the selection, shows the overlay, and steps
/// through segments as the wheel turns. Wheel down advances, wheel up goes
/// back; Esc, a click, or any key press ends it.
///
/// Wheel input is buffered and paced rather than applied directly: each
/// segment stays on screen at least <see cref="MinDisplayTime"/>, and at most
/// <see cref="MaxPendingSteps"/> steps queue up, so a burst of notches plays
/// back readably instead of skipping words. Opposite-direction input cancels
/// the queue first, acting as a brake.
/// </summary>
internal sealed class ReadingSession
{
    /// <summary>
    /// Closing requires a deliberate extra notch after dwelling on the last
    /// segment; trailing events of the burst that landed there are ignored.
    /// </summary>
    private static readonly TimeSpan EndConfirmDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Key-repeat from a still-held hotkey key (e.g. the R of Ctrl+Alt+R)
    /// must not count as "any key ends the session".
    /// </summary>
    private static readonly TimeSpan KeyGracePeriod = TimeSpan.FromMilliseconds(600);

    /// <summary>Every segment is visible at least this long.</summary>
    private readonly TimeSpan _minDisplayTime;

    /// <summary>Steps beyond this are discarded — a wild spin must not run away.</summary>
    private readonly int _maxPendingSteps;

    private readonly double _fontSize;

    private IReadOnlyList<string> _segments = Array.Empty<string>();
    private OverlayWindow? _overlay;
    private MouseHook? _mouse;
    private KeyboardHook? _keyboard;
    private DispatcherTimer? _pumpTimer;
    private int _index;
    private int _maxIndexReached;
    private int _wheelAccumulator;
    private int _pendingSteps;
    private DateTime _startedAt;
    private DateTime _lastAdvanceAt;
    private bool _ended;

    public bool IsActive { get; private set; }

    public event Action? Ended;

    public ReadingSession(Settings settings)
    {
        _minDisplayTime = TimeSpan.FromMilliseconds(settings.MinDisplayMs);
        _maxPendingSteps = settings.MaxPendingSteps;
        _fontSize = settings.FontSize;
    }

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
        _startedAt = DateTime.UtcNow;
        _lastAdvanceAt = _startedAt;

        _overlay = new OverlayWindow();
        _overlay.SetFontSize(_fontSize);
        _overlay.ShowAt(cursor);
        _overlay.SetSegment(_segments[0], 0, _segments.Count, revisit: false);

        _mouse = new MouseHook();
        _mouse.Wheel += OnWheel;
        _mouse.ButtonDown += End;
        _mouse.Install();

        _keyboard = new KeyboardHook();
        _keyboard.EscapePressed += End;
        _keyboard.OtherKeyDown += () =>
        {
            if (DateTime.UtcNow - _startedAt >= KeyGracePeriod) End();
        };
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
        if (steps == 0 || !IsActive) return;

        if (steps > 0 && _pendingSteps == 0 && _index == _segments.Count - 1)
        {
            if (DateTime.UtcNow - _lastAdvanceAt >= EndConfirmDelay) End();
            return;
        }

        _pendingSteps = Math.Clamp(_pendingSteps + steps, -_maxPendingSteps, _maxPendingSteps);
        Pump();
    }

    private void Pump()
    {
        if (_pendingSteps != 0 && DateTime.UtcNow - _lastAdvanceAt >= _minDisplayTime)
            ApplyOneStep();

        if (_pendingSteps != 0)
        {
            _pumpTimer ??= CreatePumpTimer();
            if (!_pumpTimer.IsEnabled) _pumpTimer.Start();
        }
        else
        {
            _pumpTimer?.Stop();
        }
    }

    private DispatcherTimer CreatePumpTimer()
    {
        var timer = new DispatcherTimer { Interval = _minDisplayTime };
        timer.Tick += (_, _) =>
        {
            if (_pendingSteps != 0) ApplyOneStep();
            if (_pendingSteps == 0) timer.Stop();
        };
        return timer;
    }

    private void ApplyOneStep()
    {
        var sign = Math.Sign(_pendingSteps);
        _pendingSteps -= sign;
        var next = _index + sign;
        if (next < 0 || next >= _segments.Count)
        {
            // Hitting either edge absorbs whatever is left of the burst.
            _pendingSteps = 0;
            return;
        }

        var revisit = next < _maxIndexReached;
        _index = next;
        _maxIndexReached = Math.Max(_maxIndexReached, next);
        _lastAdvanceAt = DateTime.UtcNow;
        _overlay?.SetSegment(_segments[next], next, _segments.Count, revisit);
    }

    public void End()
    {
        if (_ended) return;
        _pumpTimer?.Stop();
        _pumpTimer = null;
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
