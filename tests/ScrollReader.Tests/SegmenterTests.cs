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
        // 私/は and 本/を merge into bunsetsu; the two short bunsetsu then
        // merge again in the length-balancing pass.
        var segments = Segmenter.Segment("私は本を読んだ。");
        Assert.Equal(new[] { "私は本を", "読んだ。" }, segments);
    }

    [Fact]
    public void JapaneseBalancesSegmentLengths()
    {
        // 坊っちゃん冒頭。ほとんどの文節が 3〜9 文字に収まること。
        const string text = "親譲りの無鉄砲で小供の時から損ばかりしている。小学校に居る時分学校の二階から飛び降りて一週間ほど腰を抜かした事がある。";
        var segments = Segmenter.Segment(text);
        Assert.Equal(text, string.Concat(segments));
        var joined = string.Join("|", segments);
        var within = segments.Count(s => s.Length is >= 3 and <= 8);
        Assert.True(within >= segments.Count * 0.6, $"only {within}/{segments.Count} in range: {joined}");
        var tiny = segments.Count(s => s.Length <= 2);
        Assert.True(tiny <= segments.Count * 0.2, $"too many tiny segments: {joined}");
    }

    [Fact]
    public void JapaneseFunctionChainsStopGrowingNearMaxLength()
    {
        var segments = Segmenter.Segment("生徒が囃したからである。");
        Assert.Equal("生徒が囃したからである。", string.Concat(segments));
        Assert.All(segments, s => Assert.True(s.Length <= 8, $"too long: {s}"));
    }

    [Fact]
    public void MinSegmentLengthOneDisablesBalancing()
    {
        const string text = "私は本を読んだ。";
        var merged = Segmenter.Segment(text, minLength: 3);
        var unmerged = Segmenter.Segment(text, minLength: 1);
        Assert.Equal(text, string.Concat(unmerged));
        Assert.True(unmerged.Count > merged.Count, $"{unmerged.Count} vs {merged.Count}");
    }

    [Fact]
    public void MaxSegmentLengthIsAdjustable()
    {
        const string text = "視線を移動させずに読書速度を高める。";
        var narrow = Segmenter.Segment(text, maxLength: 4);
        var wide = Segmenter.Segment(text, maxLength: 12);
        Assert.Equal(text, string.Concat(narrow));
        Assert.Equal(text, string.Concat(wide));
        Assert.True(narrow.Count > wide.Count);
    }

    [Fact]
    public void JapaneseDoesNotMergeAcrossPausePunctuation()
    {
        var segments = Segmenter.Segment("しかし、庭は広い。");
        Assert.Equal("しかし、庭は広い。", string.Concat(segments));
        // 、 で終わる文節に後続がくっつかないこと
        var index = segments.ToList().FindIndex(s => s.EndsWith('、'));
        Assert.True(index >= 0 && index < segments.Count - 1);
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
        Assert.Equal("彼は「はい」と答えた。", string.Concat(segments));
        // 開き括弧が文節の末尾に取り残されないこと
        Assert.DoesNotContain(segments, s => s.EndsWith('「'));
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
