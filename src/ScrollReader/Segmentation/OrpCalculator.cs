namespace ScrollReader.Segmentation;

/// <summary>
/// Optimal Recognition Point for English RSVP: the character the eye should
/// land on, slightly left of the word's center (Spritz-style heuristic).
/// </summary>
public static class OrpCalculator
{
    public static int PivotIndex(string word)
    {
        // Skip leading/trailing punctuation ("dog.", "(hello") when locating
        // the core letters the heuristic applies to.
        var start = 0;
        while (start < word.Length && !char.IsLetterOrDigit(word[start])) start++;
        if (start == word.Length) return Math.Max(0, (word.Length - 1) / 2); // symbols only
        var end = word.Length - 1;
        while (end > start && !char.IsLetterOrDigit(word[end])) end--;

        var offset = (end - start + 1) switch
        {
            1 => 0,
            <= 5 => 1,
            <= 9 => 2,
            <= 13 => 3,
            _ => 4,
        };
        return start + offset;
    }
}
