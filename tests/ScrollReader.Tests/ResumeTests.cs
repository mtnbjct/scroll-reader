using Xunit;

namespace ScrollReader.Tests;

public class ResumeTests
{
    private static readonly string[] Segments = { "私は本を", "読んだ。", "面白かった。" };
    // offsets: 0-3, 4-7, 8-13

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    public void FindsSegmentContainingOffset(int offset, int expected)
    {
        Assert.Equal(expected, ReadingSession.FindSegmentIndex(Segments, offset));
    }

    [Fact]
    public void OffsetPastTheEndClampsToLastSegment()
    {
        Assert.Equal(2, ReadingSession.FindSegmentIndex(Segments, 999));
    }

    [Fact]
    public void OffsetSurvivesResegmentationWithDifferentMaxLength()
    {
        // Resuming re-segments with current settings; the character offset
        // must land near the same place regardless of segment boundaries.
        const string text = "視線を移動させずに、同じ位置に順次表示される短い文字列を読む。";
        var narrow = ScrollReader.Segmentation.Segmenter.Segment(text, maxLength: 4);
        var offset = narrow.Take(3).Sum(s => s.Length);
        var wide = ScrollReader.Segmentation.Segmenter.Segment(text, maxLength: 12);
        var index = ReadingSession.FindSegmentIndex(wide, offset);
        var wideOffset = wide.Take(index).Sum(s => s.Length);
        Assert.InRange(wideOffset, 0, offset); // lands at or just before the same character
        Assert.True(offset - wideOffset < 12, "resumed too far back");
    }
}
