using System.IO;
using NMeCab.Specialized;

namespace ScrollReader.Segmentation;

/// <summary>
/// Morphological tokenizer backed by NMeCab (pure .NET MeCab port) with the
/// bundled IPA dictionary. Part-of-speech tags drive bunsetsu attachment,
/// which is considerably more accurate than the OS word segmenter.
/// </summary>
internal sealed class MeCabTokenizer : IJapaneseTokenizer
{
    private readonly MeCabIpaDicTagger _tagger;

    private MeCabTokenizer(MeCabIpaDicTagger tagger) => _tagger = tagger;

    /// <summary>Null when the bundled dictionary cannot be loaded.</summary>
    public static MeCabTokenizer? TryCreate()
    {
        try
        {
            // Resolve the dictionary relative to this assembly, not the
            // current directory — the app may be autostarted from anywhere.
            var baseDir = Path.GetDirectoryName(typeof(MeCabTokenizer).Assembly.Location);
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppContext.BaseDirectory;
            return new MeCabTokenizer(MeCabIpaDicTagger.Create(Path.Combine(baseDir, "IpaDic")));
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<JaToken> Tokenize(string text)
    {
        var result = new List<JaToken>();
        var lineIndex = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var firstInLine = lineIndex++ > 0;
            if (line.Length == 0) continue;

            var pendingBreak = false;
            foreach (var node in _tagger.Parse(line))
            {
                if (node.PartsOfSpeech == "記号" && node.PartsOfSpeechSection1 == "空白")
                {
                    pendingBreak = true;
                    continue;
                }
                // RLength includes leading whitespace the analyzer skipped.
                var breakBefore = firstInLine || pendingBreak || node.RLength > node.Length;
                result.Add(new JaToken(node.Surface, Classify(node), breakBefore));
                firstInLine = false;
                pendingBreak = false;
            }
        }
        return result;
    }

    private static JaTokenKind Classify(MeCabIpaDicNode node) => node.PartsOfSpeech switch
    {
        "助詞" or "助動詞" => JaTokenKind.Attach,
        "接頭詞" => JaTokenKind.AttachNext,
        "記号" => node.PartsOfSpeechSection1 == "括弧開" ? JaTokenKind.AttachNext : JaTokenKind.Glue,
        "動詞" or "形容詞" or "名詞" when node.PartsOfSpeechSection1 is "非自立" or "接尾"
            => JaTokenKind.Attach,
        _ => JaTokenKind.Content,
    };
}
