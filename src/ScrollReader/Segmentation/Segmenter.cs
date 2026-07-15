using Windows.Data.Text;

namespace ScrollReader.Segmentation;

/// <summary>
/// Splits text into RSVP display units: whitespace-delimited words for
/// English, bunsetsu-like chunks for Japanese (OS word segmentation plus a
/// heuristic that merges particles/auxiliaries into the preceding word).
/// </summary>
public static class Segmenter
{
    /// <summary>
    /// Japanese segments aim for 3–8 characters: neighbours merge while one
    /// of them is shorter than the minimum, and function-word chains stop
    /// growing at the maximum. Segments outside the range can still occur
    /// (long single tokens, pause punctuation) — that beats unnatural cuts.
    /// </summary>
    private const int MinTargetLength = 3;

    private const int MaxTargetLength = 8;

    public static IReadOnlyList<string> Segment(string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return Array.Empty<string>();
        return ContainsJapanese(text) ? SegmentJapanese(text) : SegmentByWhitespace(text);
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

    private static IReadOnlyList<string> SegmentJapanese(string text)
    {
        var tokens = new WordsSegmenter("ja").GetTokens(text);
        var result = new List<string>();
        var hardBreakBefore = new List<bool>(); // whitespace/newline preceded the segment
        var pendingPrefix = "";   // opening brackets waiting for the next token
        var boundary = false;     // whitespace seen since the last token
        var cursor = 0;

        foreach (var token in tokens)
        {
            var start = (int)token.SourceTextSegment.StartPosition;
            var length = (int)token.SourceTextSegment.Length;

            // Characters the segmenter skipped (punctuation, whitespace, symbols).
            foreach (var c in text.AsSpan(cursor, start - cursor))
            {
                if (char.IsWhiteSpace(c))
                {
                    boundary = true;
                }
                else if (IsOpeningBracket(c))
                {
                    pendingPrefix += c;
                }
                else if (result.Count > 0 && pendingPrefix.Length == 0)
                {
                    result[^1] += c; // 。、！？」など closing punctuation sticks to the previous chunk
                }
                else
                {
                    pendingPrefix += c;
                }
            }

            // Use the source substring, not token.Text, which may be normalized.
            var word = text.Substring(start, length);
            var merge = pendingPrefix.Length == 0
                && !boundary
                && result.Count > 0
                && FunctionWords.Contains(word)
                && result[^1].Length + word.Length <= MaxTargetLength
                && !EndsWithPause(result[^1]);

            if (merge)
            {
                result[^1] += word;
            }
            else
            {
                result.Add(pendingPrefix + word);
                hardBreakBefore.Add(boundary);
                pendingPrefix = "";
            }
            boundary = false;
            cursor = start + length;
        }

        // Trailing punctuation after the last token.
        foreach (var c in text.AsSpan(cursor))
        {
            if (!char.IsWhiteSpace(c) && result.Count > 0) result[^1] += c;
        }
        if (pendingPrefix.Length > 0)
        {
            if (result.Count > 0) result[^1] += pendingPrefix;
            else
            {
                result.Add(pendingPrefix);
                hardBreakBefore.Add(false);
            }
        }
        return BalanceLengths(result, hardBreakBefore);
    }

    /// <summary>
    /// Second pass: pulls too-short bunsetsu together so most segments land
    /// in the 3–8 character sweet spot. Never merges across whitespace,
    /// newlines, or pause punctuation (。、！？…).
    /// </summary>
    private static List<string> BalanceLengths(List<string> segments, List<bool> hardBreakBefore)
    {
        var result = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var current = segments[i];
            var mergeable = result.Count > 0
                && !hardBreakBefore[i]
                && !EndsWithPause(result[^1])
                && (result[^1].Length < MinTargetLength || current.Length < MinTargetLength)
                && result[^1].Length + current.Length <= MaxTargetLength;

            if (mergeable) result[^1] += current;
            else result.Add(current);
        }
        return result;
    }

    private static bool IsOpeningBracket(char c) =>
        c is '「' or '『' or '（' or '(' or '【' or '〈' or '《' or '〔' or '［' or '[' or '｛' or '{' or '“' or '‘' or '"' or '\'';

    private static bool EndsWithPause(string s) =>
        s.Length > 0 && s[^1] is '。' or '、' or '！' or '？' or '!' or '?' or '…' or '.' or ',' or '，' or '．';

    /// <summary>
    /// Particles, auxiliary verbs, and formal nouns that attach to the
    /// preceding content word to approximate bunsetsu boundaries.
    /// </summary>
    private static readonly HashSet<string> FunctionWords = new(StringComparer.Ordinal)
    {
        // 格助詞・係助詞・副助詞・終助詞
        "は", "が", "を", "に", "へ", "と", "で", "も", "の", "や", "か", "ね", "よ", "な", "ぞ", "ぜ", "わ", "さ",
        "から", "まで", "より", "ほど", "くらい", "ぐらい", "など", "なり", "だけ", "しか", "ばかり", "こそ", "さえ",
        "って", "とか", "でも", "かな", "かしら",
        // 複合助詞
        "では", "には", "とは", "へは", "にも", "かも", "のは", "のが", "のを", "への", "までに", "からは",
        // 接続助詞
        "て", "で", "ば", "と", "ても", "たら", "なら", "ので", "のに", "けど", "けれど", "けれども", "し",
        "たり", "つつ", "ながら", "ものの",
        // 助動詞・補助的な語尾
        "だ", "です", "ます", "ました", "ません", "でした", "だった", "だろう", "でしょう", "た", "ぬ", "ん",
        "ない", "なかった", "なく", "なけれ", "ず", "う", "よう", "まい", "たい", "たく", "たかった",
        "らしい", "そう", "よう", "みたい", "べき", "はず",
        "れ", "られ", "せ", "させ", "れる", "られる", "せる", "させる",
        // 補助動詞・形式名詞
        "いる", "いた", "いて", "います", "いました", "いない", "ある", "あった", "あります", "ありました",
        "おり", "おります", "しまう", "しまった", "ください", "くださって", "いただく", "いただき", "いただいた",
        "する", "した", "して", "します", "しました", "され", "される", "された", "されて", "できる", "できた", "でき",
        "こと", "もの", "ため", "わけ", "つもり", "ところ",
        // 接続的な複合表現
        "という", "といった", "として", "とともに", "について", "における", "によって", "による", "に対して", "かどうか",
    };
}
