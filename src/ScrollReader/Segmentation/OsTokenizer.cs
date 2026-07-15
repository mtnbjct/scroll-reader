using Windows.Data.Text;

namespace ScrollReader.Segmentation;

/// <summary>
/// Fallback tokenizer using the OS-bundled word segmenter
/// (Windows.Data.Text.WordsSegmenter). No POS information, so attachment is
/// approximated with an explicit function-word list.
/// </summary>
internal sealed class OsTokenizer : IJapaneseTokenizer
{
    public IReadOnlyList<JaToken> Tokenize(string text)
    {
        var tokens = new WordsSegmenter("ja").GetTokens(text);
        var result = new List<JaToken>();
        var breakBefore = false;
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
                    breakBefore = true;
                    continue;
                }
                var kind = IsOpeningBracket(c) ? JaTokenKind.AttachNext : JaTokenKind.Glue;
                result.Add(new JaToken(c.ToString(), kind, breakBefore));
                breakBefore = false;
            }

            // Use the source substring, not token.Text, which may be normalized.
            var word = text.Substring(start, length);
            result.Add(new JaToken(
                word,
                FunctionWords.Contains(word) ? JaTokenKind.Attach : JaTokenKind.Content,
                breakBefore));
            breakBefore = false;
            cursor = start + length;
        }

        foreach (var c in text.AsSpan(cursor))
        {
            if (!char.IsWhiteSpace(c)) result.Add(new JaToken(c.ToString(), JaTokenKind.Glue, false));
        }
        return result;
    }

    private static bool IsOpeningBracket(char c) =>
        c is '「' or '『' or '（' or '(' or '【' or '〈' or '《' or '〔' or '［' or '[' or '｛' or '{' or '“' or '‘' or '"' or '\'';

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
        "らしい", "そう", "みたい", "べき", "はず",
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
