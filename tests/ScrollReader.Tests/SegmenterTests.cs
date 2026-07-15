using ScrollReader.Segmentation;
using Xunit;

namespace ScrollReader.Tests;

public class SegmenterTests
{
    [Fact]
    public void EnglishSplitsOnWhitespace()
    {
        var segments = Segmenter.Segment("The quick brown fox\njumps over the lazy dog.");
        Assert.Equal(
            new[] { "The", "quick", "brown", "fox", "jumps", "over", "the", "lazy", "dog." },
            segments);
    }

    [Fact]
    public void EmptyInputYieldsNoSegments()
    {
        Assert.Empty(Segmenter.Segment("   \n  "));
    }

    [Fact]
    public void JapaneseMergesParticlesIntoBunsetsu()
    {
        var segments = Segmenter.Segment("私は本を読んだ。");
        Assert.Equal(new[] { "私は", "本を", "読んだ。" }, segments);
    }

    [Fact]
    public void JapanesePreservesAllCharacters()
    {
        const string text = "視線を移動させずに、同じ位置に順次表示される短い文字列を読むことで、読書速度を高めて疲労を軽減できることが知られている。";
        var segments = Segmenter.Segment(text);
        Assert.Equal(text, string.Concat(segments));
        Assert.True(segments.Count > 5, $"expected multiple segments, got {segments.Count}");
    }

    [Fact]
    public void JapaneseSegmentsDoNotStartWithClosingPunctuation()
    {
        var segments = Segmenter.Segment("「速く読める。」と彼は言った。本当だろうか？");
        foreach (var s in segments)
        {
            Assert.False(s[0] is '。' or '、' or '」' or '！' or '？', $"segment starts with punctuation: {s}");
        }
        Assert.Equal("「速く読める。」と彼は言った。本当だろうか？", string.Concat(segments));
    }

    [Fact]
    public void OpeningBracketSticksToFollowingSegment()
    {
        var segments = Segmenter.Segment("彼は「はい」と答えた。");
        Assert.Contains(segments, s => s.StartsWith('「'));
        Assert.Equal("彼は「はい」と答えた。", string.Concat(segments));
    }

    [Fact]
    public void MixedJapaneseEnglishReconstructs()
    {
        const string text = "ScrollReaderはWindows用のRSVPリーダーです。";
        var segments = Segmenter.Segment(text);
        Assert.Equal(text, string.Concat(segments));
    }

    [Fact]
    public void DetectsJapanese()
    {
        Assert.True(Segmenter.ContainsJapanese("こんにちは"));
        Assert.True(Segmenter.ContainsJapanese("漢字"));
        Assert.False(Segmenter.ContainsJapanese("Hello, world!"));
    }
}
