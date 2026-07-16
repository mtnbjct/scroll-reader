using Xunit;

namespace ScrollReader.Tests;

public class TextCaptureTests
{
    [Fact]
    public void StripsJapaneseKindleAttribution()
    {
        const string copied = "吾輩は猫である。名前はまだ無い。\n\n夏目漱石. 吾輩は猫である (Kindle の位置No.123-125). 青空文庫. Kindle 版. ";
        Assert.Equal("吾輩は猫である。名前はまだ無い。", TextCapture.StripKindleAttribution(copied));
    }

    [Fact]
    public void StripsEnglishKindleAttribution()
    {
        const string copied = "It was the best of times, it was the worst of times.\n\nDickens, Charles. A Tale of Two Cities (Kindle Locations 10-11). Public Domain Books. Kindle Edition. ";
        Assert.Equal("It was the best of times, it was the worst of times.",
            TextCapture.StripKindleAttribution(copied));
    }

    [Fact]
    public void LeavesNormalMultilineTextAlone()
    {
        const string text = "一行目の本文。\n\n二行目の本文はKindle 版について論じているが、続きがある。";
        Assert.Equal(text, TextCapture.StripKindleAttribution(text));
    }

    [Fact]
    public void KeepsTextThatIsOnlyAnAttribution()
    {
        const string text = "夏目漱石. 坊っちゃん (Kindle の位置No.1-2). Kindle 版.";
        Assert.Equal(text, TextCapture.StripKindleAttribution(text));
    }
}
