using ScrollReader.Segmentation;
using Xunit;

namespace ScrollReader.Tests;

public class OrpCalculatorTests
{
    [Theory]
    [InlineData("a", 0)]
    [InlineData("to", 1)]
    [InlineData("dog", 1)]
    [InlineData("hello", 1)]
    [InlineData("reading", 2)]   // 6-9 letters -> 3rd
    [InlineData("wonderful", 2)]
    [InlineData("appreciated", 3)] // 10-13 letters -> 4th
    [InlineData("internationalization", 4)] // 14+ -> 5th
    public void PivotFollowsSpritzHeuristic(string word, int expected)
    {
        Assert.Equal(expected, OrpCalculator.PivotIndex(word));
    }

    [Fact]
    public void LeadingAndTrailingPunctuationAreSkipped()
    {
        Assert.Equal(2, OrpCalculator.PivotIndex("\"dog\"")); // core dog -> offset 1, +1 lead
        Assert.Equal(1, OrpCalculator.PivotIndex("dog."));
        Assert.Equal(2, OrpCalculator.PivotIndex("(hello),"));
    }

    [Fact]
    public void SymbolsOnlyFallBackToMiddle()
    {
        Assert.Equal(1, OrpCalculator.PivotIndex("---"));
        Assert.Equal(0, OrpCalculator.PivotIndex("-"));
    }

    [Fact]
    public void PivotIsAlwaysInsideTheWord()
    {
        foreach (var word in new[] { "I", "it", "x1", "don't", "co-op", "3.14" })
        {
            var pivot = OrpCalculator.PivotIndex(word);
            Assert.InRange(pivot, 0, word.Length - 1);
        }
    }
}
