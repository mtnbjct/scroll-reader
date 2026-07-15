using ScrollReader.Native;
using Xunit;

namespace ScrollReader.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void ParsesCtrlAltR()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+R", out var hk));
        Assert.Equal(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, hk.Modifiers);
        Assert.Equal(0x52u, hk.Vk); // 'R'
        Assert.False(hk.IsMiddleClick);
    }

    [Fact]
    public void ParsesCaseInsensitiveWithSpace()
    {
        Assert.True(HotkeyParser.TryParse("ctrl + shift + space", out var hk));
        Assert.Equal(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, hk.Modifiers);
        Assert.Equal(0x20u, hk.Vk); // VK_SPACE
    }

    [Fact]
    public void ParsesDigitKey()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+1", out var hk));
        Assert.Equal(0x31u, hk.Vk); // '1'
    }

    [Fact]
    public void AllowsBareFunctionKey()
    {
        Assert.True(HotkeyParser.TryParse("F9", out var hk));
        Assert.Equal(0u, hk.Modifiers);
        Assert.Equal(0x78u, hk.Vk); // VK_F9
    }

    [Fact]
    public void ParsesCtrlMiddleClick()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+MiddleClick", out var hk));
        Assert.True(hk.IsMiddleClick);
        Assert.Equal(NativeMethods.MOD_CONTROL, hk.Modifiers);
    }

    [Fact]
    public void ParsesBareMiddleClick()
    {
        Assert.True(HotkeyParser.TryParse("MiddleClick", out var hk));
        Assert.True(hk.IsMiddleClick);
        Assert.Equal(0u, hk.Modifiers);
    }

    [Theory]
    [InlineData("R")]            // bare letter would shadow typing
    [InlineData("Ctrl+")]        // missing key
    [InlineData("Foo+R")]        // unknown modifier
    [InlineData("")]
    [InlineData("Ctrl+NoSuchKey")]
    public void RejectsInvalidSpecs(string spec)
    {
        Assert.False(HotkeyParser.TryParse(spec, out _));
    }
}
