using System.Windows.Threading;
using ScrollReader.Native;
using ScrollReader.Segmentation;

namespace ScrollReader;

/// <summary>
/// One reading session: captures the selection, shows the overlay, and steps
/// through segments as the wheel turns. Esc, a click, or any key press ends it.
///
/// Two wheel modes:
/// - cruise (default): wheel down starts auto-advance and each further notch
///   speeds it up one level (throttle); wheel up slows down one level, stops
///   at level 0, and — after a short grace — steps backward notch by notch.
/// - step: one notch = one segment. Input is buffered and paced (min display
///   time per segment, bounded queue) so a burst plays back readably instead
///   of skipping words.
/// </summary>
internal sealed class ReadingSession
{
    /// <summary>
    /// Closing requires a deliberate extra notch after dwelling on the last
    /// segment; trailing events of the burst that landed there are ignored.
    /// </summary>
    private static readonly TimeSpan EndConfirmDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// After cruise decelerates to a stop, further up-notches from the same
    /// gesture must not immediately start rewinding.
    /// </summary>
    private static readonly TimeSpan RewindGrace = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Key-repeat from a still-held hotkey key (e.g. the R of Ctrl+Alt+R)
    /// must not count as "any key ends the session".
    /// </summary>
    private static readonly TimeSpan KeyGracePeriod = TimeSpan.FromMilliseconds(600);

    /// <summary>Per-level speed-up factor for cruise intervals.</summary>
    internal const double CruiseAccel = 0.75;

    internal const int CruiseLevelCap = 12;

    private readonly TimeSpan _minDisplayTime;
    private readonly int _maxPendingSteps;
    private readonly double _fontSize;
    private readonly bool _cruiseMode;
    private readonly double _cruiseBaseMs;
    private readonly int _maxCruiseLevel;
    private readonly bool _abortOnMiddleClick;
    private readonly int _maxSegmentLength;

    private IReadOnlyList<string> _segments = Array.Empty<string>();
    private OverlayWindow? _overlay;
    private MouseHook? _mouse;
    private KeyboardHook? _keyboard;
    private DispatcherTimer? _pumpTimer;
    private DispatcherTimer? _cruiseTimer;
    private int _index;
    private int _maxIndexReached;
    private int _wheelAccumulator;
    private int _pendingSteps;
    private int _cruiseLevel;
    private DateTime _startedAt;
    private DateTime _lastAdvanceAt;
    private DateTime _pausedAt;
    private bool _ended;

    public bool IsActive { get; private set; }

    public event Action? Ended;

    public ReadingSession(Settings settings, bool middleClickActivation)
    {
        _minDisplayTime = TimeSpan.FromMilliseconds(settings.MinDisplayMs);
        _maxPendingSteps = settings.MaxPendingSteps;
        _fontSize = settings.FontSize;
        _cruiseMode = settings.WheelMode != "step";
        _cruiseBaseMs = settings.CruiseBaseMs;
        _maxCruiseLevel = ComputeMaxCruiseLevel(_cruiseBaseMs, settings.MinDisplayMs);
        _abortOnMiddleClick = !middleClickActivation;
        _maxSegmentLength = settings.MaxSegmentLength;
    }

    internal static double CruiseIntervalMs(double baseMs, double floorMs, int level) =>
        Math.Max(floorMs, baseMs * Math.Pow(CruiseAccel, level - 1));

    internal static int ComputeMaxCruiseLevel(double baseMs, double floorMs)
    {
        var level = 1;
        while (level < CruiseLevelCap && CruiseIntervalMs(baseMs, floorMs, level) > floorMs) level++;
        return level;
    }

    public void Start()
    {
        NativeMethods.GetCursorPos(out var pt);
        var cursor = new System.Drawing.Point(pt.X, pt.Y);

        var text = TextCapture.CaptureSelection();
        var segments = text is null ? Array.Empty<string>() : Segmenter.Segment(text, _maxSegmentLength);
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
        _pausedAt = _startedAt;

        _overlay = new OverlayWindow();
        _overlay.SetFontSize(_fontSize);
        _overlay.ShowAt(cursor);
        UpdateOverlay();

        _mouse = new MouseHook(_abortOnMiddleClick);
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

        if (_cruiseMode) HandleCruiseInput(steps);
        else HandleStepInput(steps);
    }

    // ---- cruise mode -----------------------------------------------------

    private void HandleCruiseInput(int steps)
    {
        if (steps > 0)
        {
            // A deliberate extra notch while stopped on the last segment closes.
            if (_cruiseLevel == 0 && _index == _segments.Count - 1)
            {
                if (DateTime.UtcNow - _lastAdvanceAt >= EndConfirmDelay) End();
                return;
            }

            // Direction change cancels any queued rewind steps.
            _pendingSteps = 0;
            _pumpTimer?.Stop();

            var wasStopped = _cruiseLevel == 0;
            _cruiseLevel = Math.Min(_cruiseLevel + steps, _maxCruiseLevel);
            RunCruise(immediateFirstStep: wasStopped);
        }
        else if (_cruiseLevel > 0)
        {
            _cruiseLevel = Math.Max(0, _cruiseLevel + steps);
            if (_cruiseLevel == 0) StopCruise();
            else RunCruise(immediateFirstStep: false);
        }
        else
        {
            // Stopped: up-notches rewind, unless they are leftover momentum
            // from the gesture that decelerated to the stop.
            if (DateTime.UtcNow - _pausedAt < RewindGrace) return;
            _pendingSteps = Math.Clamp(_pendingSteps + steps, -_maxPendingSteps, _maxPendingSteps);
            Pump();
        }
    }

    private void RunCruise(bool immediateFirstStep)
    {
        _cruiseTimer ??= CreateCruiseTimer();
        _cruiseTimer.Interval = TimeSpan.FromMilliseconds(
            CruiseIntervalMs(_cruiseBaseMs, _minDisplayTime.TotalMilliseconds, _cruiseLevel));
        if (immediateFirstStep && DateTime.UtcNow - _lastAdvanceAt >= _minDisplayTime) CruiseTick();
        if (_cruiseLevel > 0)
        {
            if (!_cruiseTimer.IsEnabled) _cruiseTimer.Start();
            UpdateOverlay(); // show the new ▶ level right away
        }
    }

    private DispatcherTimer CreateCruiseTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => CruiseTick();
        return timer;
    }

    private void CruiseTick()
    {
        if (_index >= _segments.Count - 1)
        {
            StopCruise();
            return;
        }
        MoveTo(_index + 1);
        if (_index >= _segments.Count - 1) StopCruise();
    }

    private void StopCruise()
    {
        _cruiseLevel = 0;
        _cruiseTimer?.Stop();
        _pausedAt = DateTime.UtcNow;
        UpdateOverlay();
    }

    // ---- step mode (and cruise-mode rewind) ------------------------------

    private void HandleStepInput(int steps)
    {
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
        MoveTo(next);
    }

    // ---- shared ----------------------------------------------------------

    private void MoveTo(int next)
    {
        _index = next;
        _maxIndexReached = Math.Max(_maxIndexReached, next);
        _lastAdvanceAt = DateTime.UtcNow;
        UpdateOverlay();
    }

    private void UpdateOverlay() =>
        _overlay?.SetSegment(_segments[_index], _index, _segments.Count,
            revisit: _index < _maxIndexReached, cruiseLevel: _cruiseLevel);

    public void End()
    {
        if (_ended) return;
        _pumpTimer?.Stop();
        _pumpTimer = null;
        _cruiseTimer?.Stop();
        _cruiseTimer = null;
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
