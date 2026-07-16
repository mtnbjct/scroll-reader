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

    private static double Weight(string segment, double lengthWeight = 0.05) =>
        ReadingSession.DisplayWeight(segment, lengthWeight, 1.7, 1.35);

    [Fact]
    public void SentenceEndsDwellLongerThanClauseEndsThanPlain()
    {
        Assert.True(Weight("読んだ") < Weight("読んで、"));
        Assert.True(Weight("読んで、") < Weight("読んだ。"));
    }

    [Fact]
    public void PauseFactorsAreConfigurable()
    {
        var strong = ReadingSession.DisplayWeight("読んだ。", 0, 3.0, 1.35);
        var neutral = ReadingSession.DisplayWeight("読んだ。", 0, 1.0, 1.0);
        Assert.Equal(3.0, strong, 3);
        Assert.Equal(1.0, neutral, 3);
        Assert.Equal(2.0, ReadingSession.DisplayWeight("読んで、", 0, 1.7, 2.0), 3);
    }

    [Fact]
    public void DisplayTimeScalesWithLength()
    {
        var w2 = Weight("時に");
        var w4 = Weight("親譲りの");
        var w7 = Weight("一週間ほど腰を");
        Assert.True(w2 < w4);
        Assert.True(w4 < w7);
        Assert.Equal(1.0, w4); // reference length 4 is neutral
        Assert.Equal(1.15, w7, 3);
        Assert.Equal(0.9, w2, 3);
    }

    [Fact]
    public void ZeroLengthWeightIsLengthNeutral()
    {
        Assert.Equal(Weight("時に", 0), Weight("一週間ほど腰を", 0));
    }

    [Fact]
    public void LengthFactorIsClamped()
    {
        var veryLong = new string('あ', 60);
        Assert.Equal(2.0, Weight(veryLong, 0.3));
    }
}
