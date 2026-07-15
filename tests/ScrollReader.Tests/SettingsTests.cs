using Xunit;

namespace ScrollReader.Tests;

public class SettingsTests
{
    [Fact]
    public void LoadsJsonWithComments()
    {
        const string json = """
            {
              // コメント付きでも読める
              "hotkey": "Ctrl+Shift+Space",
              "minDisplayMs": 200,
              "maxPendingSteps": 3,
              "fontSize": 60,
              "blockedProcesses": ["Photoshop", "game.exe"],
            }
            """;
        var s = Settings.Load(json);
        Assert.Equal("Ctrl+Shift+Space", s.Hotkey);
        Assert.Equal(200, s.MinDisplayMs);
        Assert.Equal(3, s.MaxPendingSteps);
        Assert.Equal(60, s.FontSize);
        Assert.Equal(new[] { "Photoshop", "game.exe" }, s.BlockedProcesses);
    }

    [Fact]
    public void SanitizesOutOfRangeValues()
    {
        var s = new Settings
        {
            Hotkey = "  ",
            MinDisplayMs = 1,
            MaxPendingSteps = 0,
            FontSize = double.NaN,
            BlockedProcesses = new[] { " notepad ", "", "  " },
        }.Sanitized();

        Assert.Equal("Ctrl+Alt+R", s.Hotkey);
        Assert.Equal(30, s.MinDisplayMs);
        Assert.Equal(1, s.MaxPendingSteps);
        Assert.Equal(44, s.FontSize);
        Assert.Equal(new[] { "notepad" }, s.BlockedProcesses);
    }

    [Fact]
    public void BlockedProcessMatchingIgnoresCaseAndExeSuffix()
    {
        var s = new Settings { BlockedProcesses = new[] { "Photoshop", "game.exe" } };
        Assert.True(s.IsBlocked("photoshop"));
        Assert.True(s.IsBlocked("Photoshop.exe"));
        Assert.True(s.IsBlocked("GAME"));
        Assert.False(s.IsBlocked("notepad"));
    }

    [Fact]
    public void EmptyJsonYieldsDefaults()
    {
        var s = Settings.Load("{}");
        Assert.Equal("Ctrl+Alt+R", s.Hotkey);
        Assert.Equal(120, s.MinDisplayMs);
        Assert.Equal(5, s.MaxPendingSteps);
        Assert.Equal(44, s.FontSize);
        Assert.Empty(s.BlockedProcesses);
    }
}
