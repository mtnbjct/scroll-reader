using ScrollReader.Native;
using Xunit;

namespace ScrollReader.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void ParsesCtrlAltR()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+R", out var mods, out var vk));
        Assert.Equal(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, mods);
        Assert.Equal(0x52u, vk); // 'R'
    }

    [Fact]
    public void ParsesCaseInsensitiveWithSpace()
    {
        Assert.True(HotkeyParser.TryParse("ctrl + shift + space", out var mods, out var vk));
        Assert.Equal(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, mods);
        Assert.Equal(0x20u, vk); // VK_SPACE
    }

    [Fact]
    public void ParsesDigitKey()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+1", out _, out var vk));
        Assert.Equal(0x31u, vk); // '1'
    }

    [Fact]
    public void AllowsBareFunctionKey()
    {
        Assert.True(HotkeyParser.TryParse("F9", out var mods, out var vk));
        Assert.Equal(0u, mods);
        Assert.Equal(0x78u, vk); // VK_F9
    }

    [Theory]
    [InlineData("R")]            // bare letter would shadow typing
    [InlineData("Ctrl+")]        // missing key
    [InlineData("Foo+R")]        // unknown modifier
    [InlineData("")]
    [InlineData("Ctrl+NoSuchKey")]
    public void RejectsInvalidSpecs(string spec)
    {
        Assert.False(HotkeyParser.TryParse(spec, out _, out _));
    }
}
