using System.Text.Json;

namespace ScrollReader;

/// <summary>User-editable settings, loaded from settings.json.</summary>
public sealed class Settings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+R";

    /// <summary>Minimum display time per segment, in milliseconds.</summary>
    public int MinDisplayMs { get; set; } = 120;

    /// <summary>Maximum number of wheel steps that may queue up.</summary>
    public int MaxPendingSteps { get; set; } = 5;

    public double FontSize { get; set; } = 44;

    /// <summary>"cruise" (wheel down = speed up, wheel up = slow down/rewind) or "step" (one notch = one segment).</summary>
    public string WheelMode { get; set; } = "cruise";

    /// <summary>Cruise interval at speed level 1, in milliseconds; floored at MinDisplayMs.</summary>
    public int CruiseBaseMs { get; set; } = 350;

    /// <summary>How much faster each cruise level gets, in percent.</summary>
    public int CruiseAccelPercent { get; set; } = 25;

    /// <summary>Show characters read and speed when a session ends.</summary>
    public bool ShowStats { get; set; } = true;

    /// <summary>Cruise dwell multiplier for units ending a sentence (。！？…).</summary>
    public double SentencePauseFactor { get; set; } = 1.7;

    /// <summary>Cruise dwell multiplier for units ending a clause (、,).</summary>
    public double ClausePauseFactor { get; set; } = 1.35;

    /// <summary>Japanese segments stop merging beyond this many characters.</summary>
    public int MaxSegmentLength { get; set; } = Segmentation.Segmenter.DefaultMaxLength;

    /// <summary>Japanese segments shorter than this try to merge with a neighbour.</summary>
    public int MinSegmentLength { get; set; } = Segmentation.Segmenter.DefaultMinLength;

    /// <summary>Highlight and align the optimal recognition point for non-Japanese text.</summary>
    public bool OrpEnabled { get; set; } = true;

    /// <summary>Japanese tokenizer: "mecab" (NMeCab + IPA dictionary) or "os" (Windows WordsSegmenter).</summary>
    public string Segmenter { get; set; } = "mecab";

    /// <summary>Cruise display-time change per character away from the reference length (0 disables).</summary>
    public double LengthWeight { get; set; } = 0.05;

    /// <summary>Process names (with or without .exe) where the hotkey is ignored.</summary>
    public string[] BlockedProcesses { get; set; } = Array.Empty<string>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Parses JSON (comments allowed) and clamps values to sane ranges.</summary>
    public static Settings Load(string json) =>
        (JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings()).Sanitized();

    public Settings Sanitized()
    {
        var maxSegment = Math.Clamp(MaxSegmentLength, 4, 20);
        return new()
        {
            Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "Ctrl+Alt+R" : Hotkey.Trim(),
            MinDisplayMs = Math.Clamp(MinDisplayMs, 30, 2000),
            MaxPendingSteps = Math.Clamp(MaxPendingSteps, 1, 50),
            FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 12, 200) : 44,
            WheelMode = string.Equals(WheelMode?.Trim(), "step", StringComparison.OrdinalIgnoreCase) ? "step" : "cruise",
            CruiseBaseMs = Math.Clamp(CruiseBaseMs, 100, 3000),
            CruiseAccelPercent = Math.Clamp(CruiseAccelPercent, 5, 50),
            ShowStats = ShowStats,
            SentencePauseFactor = double.IsFinite(SentencePauseFactor) ? Math.Clamp(SentencePauseFactor, 1.0, 4.0) : 1.7,
            ClausePauseFactor = double.IsFinite(ClausePauseFactor) ? Math.Clamp(ClausePauseFactor, 1.0, 4.0) : 1.35,
            MaxSegmentLength = maxSegment,
            MinSegmentLength = Math.Clamp(MinSegmentLength, 1, Math.Min(8, maxSegment)),
            OrpEnabled = OrpEnabled,
            Segmenter = string.Equals(Segmenter?.Trim(), "os", StringComparison.OrdinalIgnoreCase) ? "os" : "mecab",
            LengthWeight = double.IsFinite(LengthWeight) ? Math.Clamp(LengthWeight, 0, 0.3) : 0.05,
            BlockedProcesses = (BlockedProcesses ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToArray(),
        };
    }

    public bool IsBlocked(string processName) =>
        BlockedProcesses.Any(p =>
            string.Equals(TrimExe(p), TrimExe(processName), StringComparison.OrdinalIgnoreCase));

    private static string TrimExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
