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
        Assert.Equal("cruise", s.WheelMode);
        Assert.Equal(350, s.CruiseBaseMs);
        Assert.Equal(25, s.CruiseAccelPercent);
        Assert.True(s.ShowStats);
        Assert.Equal(1.7, s.SentencePauseFactor);
        Assert.Equal(1.35, s.ClausePauseFactor);
        Assert.Equal(3, s.MinSegmentLength);
        Assert.Equal(7, s.MaxSegmentLength);
        Assert.True(s.OrpEnabled);
        Assert.Equal("mecab", s.Segmenter);
        Assert.Empty(s.BlockedProcesses);
    }

    [Theory]
    [InlineData("os", "os")]
    [InlineData("OS", "os")]
    [InlineData("mecab", "mecab")]
    [InlineData("typo", "mecab")]
    public void SegmenterEngineNormalizes(string input, string expected)
    {
        Assert.Equal(expected, new Settings { Segmenter = input }.Sanitized().Segmenter);
    }

    [Fact]
    public void MaxSegmentLengthIsClamped()
    {
        Assert.Equal(4, new Settings { MaxSegmentLength = 1 }.Sanitized().MaxSegmentLength);
        Assert.Equal(20, new Settings { MaxSegmentLength = 99 }.Sanitized().MaxSegmentLength);
    }

    [Fact]
    public void CruiseAccelPercentIsClamped()
    {
        Assert.Equal(5, new Settings { CruiseAccelPercent = 0 }.Sanitized().CruiseAccelPercent);
        Assert.Equal(50, new Settings { CruiseAccelPercent = 90 }.Sanitized().CruiseAccelPercent);
    }

    [Fact]
    public void PauseFactorsAreClamped()
    {
        Assert.Equal(1.0, new Settings { SentencePauseFactor = 0.2 }.Sanitized().SentencePauseFactor);
        Assert.Equal(4.0, new Settings { ClausePauseFactor = 99 }.Sanitized().ClausePauseFactor);
        Assert.Equal(1.7, new Settings { SentencePauseFactor = double.NaN }.Sanitized().SentencePauseFactor);
    }

    [Fact]
    public void MinSegmentLengthIsClampedAndCannotExceedMax()
    {
        Assert.Equal(1, new Settings { MinSegmentLength = 0 }.Sanitized().MinSegmentLength);
        Assert.Equal(8, new Settings { MinSegmentLength = 99, MaxSegmentLength = 20 }.Sanitized().MinSegmentLength);
        Assert.Equal(5, new Settings { MinSegmentLength = 99, MaxSegmentLength = 5 }.Sanitized().MinSegmentLength);
    }

    [Theory]
    [InlineData("step", "step")]
    [InlineData("STEP", "step")]
    [InlineData("cruise", "cruise")]
    [InlineData("typo", "cruise")]
    [InlineData(null, "cruise")]
    public void WheelModeNormalizes(string? input, string expected)
    {
        var s = new Settings { WheelMode = input! }.Sanitized();
        Assert.Equal(expected, s.WheelMode);
    }
}
