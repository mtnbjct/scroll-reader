using System.IO;
using Xunit;

namespace ScrollReader.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void TemplateMatchesSettingsDefaults()
    {
        var fromTemplate = Settings.Load(SettingsStore.BuildTemplate());
        var defaults = new Settings().Sanitized();
        Assert.Equal(defaults.Hotkey, fromTemplate.Hotkey);
        Assert.Equal(defaults.WheelMode, fromTemplate.WheelMode);
        Assert.Equal(defaults.CruiseBaseMs, fromTemplate.CruiseBaseMs);
        Assert.Equal(defaults.LengthWeight, fromTemplate.LengthWeight);
        Assert.Equal(defaults.MaxSegmentLength, fromTemplate.MaxSegmentLength);
        Assert.Equal(defaults.Segmenter, fromTemplate.Segmenter);
        Assert.Equal(defaults.MinDisplayMs, fromTemplate.MinDisplayMs);
        Assert.Equal(defaults.MaxPendingSteps, fromTemplate.MaxPendingSteps);
        Assert.Equal(defaults.FontSize, fromTemplate.FontSize);
        Assert.Equal(defaults.OrpEnabled, fromTemplate.OrpEnabled);
        Assert.Equal(defaults.BlockedProcesses, fromTemplate.BlockedProcesses);
    }

    [Fact]
    public void EveryTemplateEntryDocumentsItsDefault()
    {
        var template = SettingsStore.BuildTemplate();
        Assert.Contains("// デフォルト: 0.05", template);       // lengthWeight
        Assert.Contains("// デフォルト: \"Ctrl+Alt+R\"", template);
        // 1 default comment per key
        var keys = template.Split('\n').Count(l => l.TrimStart().StartsWith("\""));
        var defaultComments = template.Split('\n').Count(l => l.Contains("// デフォルト: "));
        Assert.Equal(keys, defaultComments);
    }

    [Fact]
    public void AddMissingKeysPreservesCustomValues()
    {
        const string json = """
            {
              // 自分のカスタマイズ
              "minDisplayMs": 200,
              "hotkey": "Ctrl+MiddleClick",
            }
            """;
        var migrated = SettingsStore.AddMissingKeys(json);
        Assert.NotNull(migrated);
        var settings = Settings.Load(migrated!);
        Assert.Equal(200, settings.MinDisplayMs);
        Assert.Equal("Ctrl+MiddleClick", settings.Hotkey);
        Assert.Equal("mecab", settings.Segmenter);
        Assert.Equal(0.05, settings.LengthWeight);
        Assert.Contains("// 自分のカスタマイズ", migrated);
        Assert.Contains("\"lengthWeight\": 0.05", migrated);
    }

    [Fact]
    public void AddMissingKeysReturnsNullWhenComplete()
    {
        Assert.Null(SettingsStore.AddMissingKeys(SettingsStore.BuildTemplate()));
    }

    [Fact]
    public void AddMissingKeysLeavesBrokenFilesAlone()
    {
        Assert.Null(SettingsStore.AddMissingKeys("{ this is not json"));
        Assert.Null(SettingsStore.AddMissingKeys("[1, 2, 3]"));
    }

    [Fact]
    public void InitializeMigratesTheFileOnDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"srtest-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\n  \"fontSize\": 60,\n}\n");
            using (var store = new SettingsStore(path))
            {
                store.Initialize();
                Assert.Equal(60, store.Current.FontSize);       // preserved
                Assert.Equal(120, store.Current.MinDisplayMs);  // default added
            }
            var text = File.ReadAllText(path);
            Assert.Contains("\"fontSize\": 60", text);
            Assert.Contains("\"minDisplayMs\": 120", text);
            // Idempotent: a second pass changes nothing.
            Assert.Null(SettingsStore.AddMissingKeys(text));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
