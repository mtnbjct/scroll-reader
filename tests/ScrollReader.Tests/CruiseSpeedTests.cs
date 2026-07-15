using Xunit;

namespace ScrollReader.Tests;

public class CruiseSpeedTests
{
    [Fact]
    public void Level1UsesBaseInterval()
    {
        Assert.Equal(350, ReadingSession.CruiseIntervalMs(350, 120, 1));
    }

    [Fact]
    public void IntervalsShrinkMonotonicallyDownToFloor()
    {
        var previous = double.MaxValue;
        for (var level = 1; level <= 12; level++)
        {
            var interval = ReadingSession.CruiseIntervalMs(350, 120, level);
            Assert.True(interval <= previous, $"level {level} got slower");
            Assert.True(interval >= 120, $"level {level} went below the floor");
            previous = interval;
        }
    }

    [Fact]
    public void MaxLevelIsWhereTheFloorIsReached()
    {
        var max = ReadingSession.ComputeMaxCruiseLevel(350, 120);
        Assert.Equal(5, max);
        Assert.Equal(120, ReadingSession.CruiseIntervalMs(350, 120, max));
        Assert.True(ReadingSession.CruiseIntervalMs(350, 120, max - 1) > 120);
    }

    [Fact]
    public void BaseBelowFloorYieldsSingleLevel()
    {
        Assert.Equal(1, ReadingSession.ComputeMaxCruiseLevel(100, 120));
    }

    [Fact]
    public void MaxLevelIsCapped()
    {
        Assert.Equal(ReadingSession.CruiseLevelCap, ReadingSession.ComputeMaxCruiseLevel(3000, 1));
    }

    [Fact]
    public void SentenceEndsDwellLongerThanClauseEndsThanPlain()
    {
        var plain = ReadingSession.DisplayWeight("読んだ");
        var clause = ReadingSession.DisplayWeight("読んで、");
        var sentence = ReadingSession.DisplayWeight("読んだ。");
        Assert.True(plain < clause);
        Assert.True(clause < sentence);
    }

    [Fact]
    public void LongSegmentsGetASmallBoost()
    {
        Assert.True(ReadingSession.DisplayWeight("一週間ほど腰を") > ReadingSession.DisplayWeight("時から"));
        Assert.True(ReadingSession.DisplayWeight("reading") > ReadingSession.DisplayWeight("dog"));
    }
}
