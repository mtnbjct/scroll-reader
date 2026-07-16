namespace ScrollReader.Segmentation;

/// <summary>
/// Splits text into RSVP display units: whitespace-delimited words for
/// English, bunsetsu-like chunks for Japanese. Japanese tokens come from a
/// pluggable tokenizer — NMeCab with the bundled IPA dictionary by default,
/// the OS word segmenter as fallback ("segmenter": "os" in settings).
/// </summary>
public static class Segmenter
{
    /// <summary>
    /// Japanese segments aim for minLength..maxLength characters: neighbours
    /// merge while one of them is shorter than the minimum, and attachment
    /// chains stop growing at the maximum. Segments outside the range can
    /// still occur (long single tokens, pause punctuation) — that beats
    /// unnatural cuts.
    /// </summary>
    public const int DefaultMaxLength = 7;

    public const int DefaultMinLength = 3;

    private static readonly Lazy<IJapaneseTokenizer?> MeCab = new(MeCabTokenizer.TryCreate);
    private static readonly Lazy<IJapaneseTokenizer> Os = new(() => new OsTokenizer());

    public static IReadOnlyList<string> Segment(
        string text, int maxLength = DefaultMaxLength, string engine = "mecab", int minLength = DefaultMinLength)
    {
        text = text.Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return Array.Empty<string>();
        if (!ContainsJapanese(text)) return SegmentByWhitespace(text);

        var tokenizer = engine == "os" ? Os.Value : MeCab.Value ?? Os.Value;
        return SegmentJapanese(text, maxLength, minLength, tokenizer);
    }

    public static bool ContainsJapanese(string text) => text.Any(IsJapaneseChar);

    private static bool IsJapaneseChar(char c) =>
        c is >= '぀' and <= 'ゟ'   // hiragana
        or >= '゠' and <= 'ヿ'     // katakana
        or >= '一' and <= '鿿'     // CJK unified ideographs
        or >= 'ｦ' and <= 'ﾝ'     // halfwidth katakana
        or '々' or '〆';

    private static IReadOnlyList<string> SegmentByWhitespace(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyList<string> SegmentJapanese(string text, int maxLength, int minLength, IJapaneseTokenizer tokenizer)
    {
        var segments = new List<string>();
        var hardBreakBefore = new List<bool>();
        var pendingPrefix = "";   // opening brackets / prefixes waiting for the next content token
        var prefixBreak = false;

        void AddSegment(string surface, bool breakBefore)
        {
            segments.Add(pendingPrefix + surface);
            hardBreakBefore.Add(breakBefore || prefixBreak);
            pendingPrefix = "";
            prefixBreak = false;
        }

        foreach (var token in tokenizer.Tokenize(text))
        {
            switch (token.Kind)
            {
                case JaTokenKind.AttachNext:
                    if (pendingPrefix.Length == 0) prefixBreak = token.HardBreakBefore;
                    pendingPrefix += token.Surface;
                    break;

                case JaTokenKind.Glue:
                    if (pendingPrefix.Length > 0) pendingPrefix += token.Surface;
                    else if (segments.Count > 0 && !token.HardBreakBefore) segments[^1] += token.Surface;
                    else AddSegment(token.Surface, token.HardBreakBefore);
                    break;

                case JaTokenKind.Attach when pendingPrefix.Length == 0
                    && !token.HardBreakBefore
                    && segments.Count > 0
                    && segments[^1].Length + token.Surface.Length <= maxLength
                    && !EndsWithPause(segments[^1]):
                    segments[^1] += token.Surface;
                    break;

                default: // Content, or Attach that could not attach
                    AddSegment(token.Surface, token.HardBreakBefore);
                    break;
            }
        }

        if (pendingPrefix.Length > 0)
        {
            if (segments.Count > 0) segments[^1] += pendingPrefix;
            else AddSegment("", false);
        }
        return BalanceLengths(segments, hardBreakBefore, maxLength, minLength);
    }

    /// <summary>
    /// Second pass: pulls too-short bunsetsu together so most segments land
    /// in the minLength..maxLength character sweet spot. Never merges across
    /// whitespace, newlines, or pause punctuation (。、！？…).
    /// </summary>
    private static List<string> BalanceLengths(List<string> segments, List<bool> hardBreakBefore, int maxLength, int minLength)
    {
        var result = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var current = segments[i];
            if (current.Length == 0) continue;
            var mergeable = result.Count > 0
                && !hardBreakBefore[i]
                && !EndsWithPause(result[^1])
                && (result[^1].Length < minLength || current.Length < minLength)
                && result[^1].Length + current.Length <= maxLength;

            if (mergeable) result[^1] += current;
            else result.Add(current);
        }
        return result;
    }

    private static bool EndsWithPause(string s) =>
        s.Length > 0 && s[^1] is '。' or '、' or '！' or '？' or '!' or '?' or '…' or '.' or ',' or '，' or '．';
}
