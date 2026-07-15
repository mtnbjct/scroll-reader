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

    public Settings Sanitized() => new()
    {
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "Ctrl+Alt+R" : Hotkey.Trim(),
        MinDisplayMs = Math.Clamp(MinDisplayMs, 30, 2000),
        MaxPendingSteps = Math.Clamp(MaxPendingSteps, 1, 50),
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 12, 200) : 44,
        BlockedProcesses = (BlockedProcesses ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray(),
    };

    public bool IsBlocked(string processName) =>
        BlockedProcesses.Any(p =>
            string.Equals(TrimExe(p), TrimExe(processName), StringComparison.OrdinalIgnoreCase));

    private static string TrimExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
