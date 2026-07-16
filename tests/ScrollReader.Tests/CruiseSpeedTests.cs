using Xunit;

namespace ScrollReader.Tests;

public class CruiseSpeedTests
{
    private const double Accel = 0.75; // 25%/level

    [Fact]
    public void Level1UsesBaseInterval()
    {
        Assert.Equal(350, ReadingSession.CruiseIntervalMs(350, 120, 1, Accel));
    }

    [Fact]
    public void IntervalsShrinkMonotonicallyDownToFloor()
    {
        var previous = double.MaxValue;
        for (var level = 1; level <= 12; level++)
        {
            var interval = ReadingSession.CruiseIntervalMs(350, 120, level, Accel);
            Assert.True(interval <= previous, $"level {level} got slower");
            Assert.True(interval >= 120, $"level {level} went below the floor");
            previous = interval;
        }
    }

    [Fact]
    public void MaxLevelIsWhereTheFloorIsReached()
    {
        var max = ReadingSession.ComputeMaxCruiseLevel(350, 120, Accel);
        Assert.Equal(5, max);
        Assert.Equal(120, ReadingSession.CruiseIntervalMs(350, 120, max, Accel));
        Assert.True(ReadingSession.CruiseIntervalMs(350, 120, max - 1, Accel) > 120);
    }

    [Fact]
    public void GentlerAccelYieldsMoreLevels()
    {
        var steep = ReadingSession.ComputeMaxCruiseLevel(350, 120, 0.5);  // 50%/level
        var gentle = ReadingSession.ComputeMaxCruiseLevel(350, 120, 0.95); // 5%/level
        Assert.True(gentle > steep);
    }

    [Fact]
    public void BaseBelowFloorYieldsSingleLevel()
    {
        Assert.Equal(1, ReadingSession.ComputeMaxCruiseLevel(100, 120, Accel));
    }

    [Fact]
    public void MaxLevelIsCapped()
    {
        Assert.Equal(ReadingSession.CruiseLevelCap, ReadingSession.ComputeMaxCruiseLevel(3000, 1, Accel));
    }

    [Fact]
    public void SentenceEndsDwellLongerThanClauseEndsThanPlain()
    {
        var plain = ReadingSession.DisplayWeight("読んだ", 0.05);
        var clause = ReadingSession.DisplayWeight("読んで、", 0.05);
        var sentence = ReadingSession.DisplayWeight("読んだ。", 0.05);
        Assert.True(plain < clause);
        Assert.True(clause < sentence);
    }

    [Fact]
    public void DisplayTimeScalesWithLength()
    {
        var w2 = ReadingSession.DisplayWeight("時に", 0.05);
        var w4 = ReadingSession.DisplayWeight("親譲りの", 0.05);
        var w7 = ReadingSession.DisplayWeight("一週間ほど腰を", 0.05);
        Assert.True(w2 < w4);
        Assert.True(w4 < w7);
        Assert.Equal(1.0, w4); // reference length 4 is neutral
        Assert.Equal(1.15, w7, 3);
        Assert.Equal(0.9, w2, 3);
    }

    [Fact]
    public void ZeroLengthWeightIsLengthNeutral()
    {
        Assert.Equal(
            ReadingSession.DisplayWeight("時に", 0),
            ReadingSession.DisplayWeight("一週間ほど腰を", 0));
    }

    [Fact]
    public void LengthFactorIsClamped()
    {
        var veryLong = new string('あ', 60);
        Assert.Equal(2.0, ReadingSession.DisplayWeight(veryLong, 0.3));
    }
}
