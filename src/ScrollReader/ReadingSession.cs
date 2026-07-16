using System.Windows.Threading;
using ScrollReader.Native;
using ScrollReader.Segmentation;

namespace ScrollReader;

/// <summary>Where the previous session left off, for resuming without a selection.</summary>
internal sealed record ResumeState(string Text, int CharOffset);

/// <summary>
/// One reading session: captures the selection (or resumes the previous
/// text), shows the overlay, and steps through segments as the wheel turns.
/// Esc, a click, or any non-modifier key press ends it.
///
/// Two wheel modes, with Ctrl temporarily switching to the other one:
/// - cruise (default): wheel down starts auto-advance and each further notch
///   speeds it up one level (throttle); wheel up slows down one level, stops
///   at level 0, and — after a short grace — steps backward notch by notch.
///   The cruise level is remembered across mode switches.
/// - step: one notch = one segment. Input is buffered and paced (min display
///   time per segment, bounded queue) so a burst of notches plays back
///   readably instead of skipping words.
///
/// Holding Alt while moving the mouse re-anchors the overlay.
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

    internal const int CruiseLevelCap = 12;

    /// <summary>Segment length at which the length factor is exactly 1.0.</summary>
    internal const int ReferenceLength = 4;

    private readonly TimeSpan _minDisplayTime;
    private readonly int _maxPendingSteps;
    private readonly double _fontSize;
    private readonly bool _cruiseMode;
    private readonly double _cruiseBaseMs;
    private readonly double _cruiseAccel;
    private readonly int _maxCruiseLevel;
    private readonly bool _abortOnMiddleClick;
    private readonly int _maxSegmentLength;
    private readonly bool _orpEnabled;
    private readonly string _segmenterEngine;
    private readonly double _lengthWeight;
    private readonly bool _showStats;
    private readonly ResumeState? _resume;

    private IReadOnlyList<string> _segments = Array.Empty<string>();
    private int[]? _orpIndices;
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
    private int _savedCruiseLevel;
    private bool _modifierHeld;
    private bool _ctrlDownAtStart;
    private DateTime _startedAt;
    private DateTime _lastAdvanceAt;
    private DateTime _pausedAt;
    private bool _ended;

    public bool IsActive { get; private set; }

    /// <summary>The text this session displayed (captured or resumed); null if nothing was shown.</summary>
    public string? SourceText { get; private set; }

    /// <summary>Character offset of the segment shown when the session ended.</summary>
    public int LastCharOffset { get; private set; }

    public event Action? Ended;

    public ReadingSession(Settings settings, bool middleClickActivation, ResumeState? resume = null)
    {
        _minDisplayTime = TimeSpan.FromMilliseconds(settings.MinDisplayMs);
        _maxPendingSteps = settings.MaxPendingSteps;
        _fontSize = settings.FontSize;
        _cruiseMode = settings.WheelMode != "step";
        _cruiseBaseMs = settings.CruiseBaseMs;
        _cruiseAccel = 1 - settings.CruiseAccelPercent / 100.0;
        _maxCruiseLevel = settings.MaxCruiseLevel > 0
            ? settings.MaxCruiseLevel
            : ComputeMaxCruiseLevel(_cruiseBaseMs, settings.MinDisplayMs, _cruiseAccel);
        _abortOnMiddleClick = !middleClickActivation;
        _maxSegmentLength = settings.MaxSegmentLength;
        _orpEnabled = settings.OrpEnabled;
        _segmenterEngine = settings.Segmenter;
        _lengthWeight = settings.LengthWeight;
        _showStats = settings.ShowStats;
        _resume = resume;
    }

    /// <summary>Cruise switches to step while Ctrl is held, and vice versa.</summary>
    private bool EffectiveCruise => _cruiseMode ^ _modifierHeld;

    internal static double CruiseIntervalMs(double baseMs, double floorMs, int level, double accel) =>
        Math.Max(floorMs, baseMs * Math.Pow(accel, level - 1));

    internal static int ComputeMaxCruiseLevel(double baseMs, double floorMs, double accel)
    {
        var level = 1;
        while (level < CruiseLevelCap && CruiseIntervalMs(baseMs, floorMs, level, accel) > floorMs) level++;
        return level;
    }

    /// <summary>
    /// Auto-advance display time mimics reading rhythm: proportional to
    /// segment length (lengthWeight per character around the reference
    /// length, clamped to 0.6–2.0x), times pause multipliers for sentence
    /// ends (1.7x) and clause ends (1.35x).
    /// </summary>
    internal static double DisplayWeight(string segment, double lengthWeight)
    {
        var weight = Math.Clamp(1 + lengthWeight * (segment.Length - ReferenceLength), 0.6, 2.0);
        var last = segment[^1];
        if (last is '。' or '！' or '？' or '!' or '?' or '…' or '.') weight *= 1.7;
        else if (last is '、' or ',' or '，') weight *= 1.35;
        return weight;
    }

    /// <summary>Index of the segment containing the given character offset.</summary>
    internal static int FindSegmentIndex(IReadOnlyList<string> segments, int charOffset)
    {
        var cumulative = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            if (charOffset < cumulative + segments[i].Length) return i;
            cumulative += segments[i].Length;
        }
        return Math.Max(0, segments.Count - 1);
    }

    public void Start()
    {
        NativeMethods.GetCursorPos(out var pt);
        var cursor = new System.Drawing.Point(pt.X, pt.Y);

        var text = TextCapture.CaptureSelection();
        var resumed = false;
        if (string.IsNullOrWhiteSpace(text) && _resume is not null)
        {
            text = _resume.Text;
            resumed = true;
        }
        var segments = text is null
            ? Array.Empty<string>()
            : Segmenter.Segment(text, _maxSegmentLength, _segmenterEngine);
        if (segments.Count == 0)
        {
            new OverlayWindow().ShowTransientMessage("テキストが選択されていません", cursor);
            Finish();
            return;
        }

        SourceText = text;
        _segments = segments;
        if (resumed)
        {
            _index = FindSegmentIndex(segments, _resume!.CharOffset);
            _maxIndexReached = _index;
        }
        IsActive = true;
        _startedAt = DateTime.UtcNow;
        _lastAdvanceAt = _startedAt;
        _pausedAt = _startedAt;
        _ctrlDownAtStart = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;

        _overlay = new OverlayWindow();
        _overlay.SetFontSize(_fontSize);
        if (_orpEnabled && !Segmenter.ContainsJapanese(text!))
        {
            _orpIndices = new int[segments.Count];
            int maxPrefix = 0, maxSuffix = 0;
            for (var i = 0; i < segments.Count; i++)
            {
                var pivot = OrpCalculator.PivotIndex(segments[i]);
                _orpIndices[i] = pivot;
                maxPrefix = Math.Max(maxPrefix, pivot);
                maxSuffix = Math.Max(maxSuffix, segments[i].Length - pivot - 1);
            }
            _overlay.ConfigureOrpLayout(maxPrefix, maxSuffix);
        }
        _overlay.ShowAt(cursor);
        UpdateOverlay();

        _mouse = new MouseHook(_abortOnMiddleClick);
        _mouse.Wheel += OnWheel;
        _mouse.ButtonDown += End;
        _mouse.MouseMoved += OnMouseMoved;
        _mouse.Install();

        _keyboard = new KeyboardHook();
        _keyboard.EscapePressed += End;
        _keyboard.OtherKeyDown += () =>
        {
            if (DateTime.UtcNow - _startedAt >= KeyGracePeriod) End();
        };
        _keyboard.CtrlDown += OnCtrlDown;
        _keyboard.CtrlUp += OnCtrlUp;
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

        if (EffectiveCruise) HandleCruiseInput(steps);
        else HandleStepInput(steps);
    }

    // ---- temporary mode switch (Ctrl held) --------------------------------

    private void OnCtrlDown()
    {
        // Ctrl still held from the activation hotkey (and its key-repeat)
        // must not switch modes; wait for a release first.
        if (_ctrlDownAtStart || _modifierHeld || !IsActive) return;
        _modifierHeld = true;
        SwitchEffectiveMode();
    }

    private void OnCtrlUp()
    {
        if (_ctrlDownAtStart)
        {
            _ctrlDownAtStart = false;
            return;
        }
        if (!_modifierHeld || !IsActive) return;
        _modifierHeld = false;
        SwitchEffectiveMode();
    }

    private void SwitchEffectiveMode()
    {
        if (EffectiveCruise)
        {
            // Entering cruise: resume at the remembered speed.
            _pendingSteps = 0;
            _pumpTimer?.Stop();
            _cruiseLevel = Math.Min(_savedCruiseLevel, _maxCruiseLevel);
            if (_cruiseLevel > 0) RunCruise(immediateFirstStep: false);
            else UpdateOverlay();
        }
        else
        {
            // Leaving cruise: remember the speed and stop.
            _savedCruiseLevel = _cruiseLevel;
            _cruiseTimer?.Stop();
            _cruiseLevel = 0;
            _pausedAt = DateTime.UtcNow;
            UpdateOverlay();
        }
    }

    // ---- overlay repositioning (Alt held) ---------------------------------

    private void OnMouseMoved(int x, int y)
    {
        if (!IsActive) return;
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) == 0) return;
        _overlay?.MoveAnchorTo(new System.Drawing.Point(x, y));
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
        ApplyCruiseInterval();
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
        else ApplyCruiseInterval(); // dwell longer on punctuation and long units
    }

    private void ApplyCruiseInterval()
    {
        var baseMs = CruiseIntervalMs(_cruiseBaseMs, _minDisplayTime.TotalMilliseconds, _cruiseLevel, _cruiseAccel);
        _cruiseTimer!.Interval = TimeSpan.FromMilliseconds(baseMs * DisplayWeight(_segments[_index], _lengthWeight));
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
            revisit: _index < _maxIndexReached, cruiseLevel: _cruiseLevel,
            orpIndex: _orpIndices?[_index] ?? -1);

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

        if (IsActive)
        {
            LastCharOffset = _segments.Take(_index).Sum(s => s.Length);
            MaybeShowStats();
        }
        Finish();
    }

    private void MaybeShowStats()
    {
        if (!_showStats) return;
        var duration = DateTime.UtcNow - _startedAt;
        var unitsRead = _maxIndexReached + 1;
        if (duration.TotalSeconds < 3 || unitsRead < 6) return;

        string message;
        if (_orpIndices is not null)
        {
            var wpm = unitsRead / duration.TotalMinutes;
            message = $"{unitsRead:N0} words ・ {wpm:N0} wpm";
        }
        else
        {
            var charsRead = _segments.Take(unitsRead).Sum(s => s.Length);
            var cpm = charsRead / duration.TotalMinutes;
            message = $"{charsRead:N0}字 ・ {cpm:N0}字/分";
        }
        NativeMethods.GetCursorPos(out var pt);
        new OverlayWindow().ShowTransientMessage(message, new System.Drawing.Point(pt.X, pt.Y));
    }

    private void Finish()
    {
        if (_ended) return;
        _ended = true;
        IsActive = false;
        Ended?.Invoke();
    }
}
