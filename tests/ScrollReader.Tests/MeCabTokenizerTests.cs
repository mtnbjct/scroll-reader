using ScrollReader.Segmentation;
using Xunit;

namespace ScrollReader.Tests;

public class MeCabTokenizerTests
{
    [Fact]
    public void BundledDictionaryLoads()
    {
        // If this fails, the mecab engine is silently falling back to the OS
        // segmenter everywhere.
        Assert.NotNull(MeCabTokenizer.TryCreate());
    }

    [Fact]
    public void KeepsVerbMorphemesTogether()
    {
        // The OS segmenter tokenizes 囃した as 囃し|たから, splitting inside
        // the conjugated verb. MeCab's POS-based attachment must not.
        var segments = Segmenter.Segment("弱虫やーい。と囃したからである。");
        Assert.Equal("弱虫やーい。と囃したからである。", string.Concat(segments));
        Assert.DoesNotContain(segments, s => s.EndsWith("囃し"));
    }

    [Fact]
    public void EnginesCanDiffer()
    {
        const string text = "私は本を読んだ。";
        Assert.Equal(text, string.Concat(Segmenter.Segment(text, 7, "mecab")));
        Assert.Equal(text, string.Concat(Segmenter.Segment(text, 7, "os")));
    }
}
