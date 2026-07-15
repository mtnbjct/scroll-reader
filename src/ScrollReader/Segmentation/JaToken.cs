namespace ScrollReader.Segmentation;

internal enum JaTokenKind
{
    /// <summary>Starts a new bunsetsu (content word).</summary>
    Content,

    /// <summary>Attaches to the previous bunsetsu if it fits (particles, auxiliaries, suffixes).</summary>
    Attach,

    /// <summary>Always sticks to the previous bunsetsu (closing punctuation: 。、」…).</summary>
    Glue,

    /// <summary>Sticks to the following bunsetsu (opening brackets, prefixes: 「 お〜).</summary>
    AttachNext,
}

internal readonly record struct JaToken(string Surface, JaTokenKind Kind, bool HardBreakBefore);

/// <summary>Produces Japanese tokens for the bunsetsu assembly in <see cref="Segmenter"/>.</summary>
internal interface IJapaneseTokenizer
{
    IReadOnlyList<JaToken> Tokenize(string text);
}
