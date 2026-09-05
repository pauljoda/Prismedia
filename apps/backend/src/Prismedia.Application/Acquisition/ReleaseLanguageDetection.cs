using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Detects the language(s) a release declares, from the indexer's language attribute and from language
/// tokens in the release title (FRENCH, GERMAN, ITA, MULTi, …). Canonical names are lowercase English
/// language names; user-entered preferred languages are normalized through the same alias table so
/// "eng", "English", and an indexer's "ENGLISH" all compare equal. A release that names no language is
/// treated as unmarked — assumed to be the user's top preference (English by default) — because the
/// overwhelming convention is that only non-English (or multi) audio gets tagged.
/// </summary>
public static partial class ReleaseLanguageDetection {
    /// <summary>Canonical marker for a multi-language release: it satisfies any preferred language.</summary>
    public const string Multi = "multi";

    // Alias token → canonical language name. Tokens are matched whole between separator characters,
    // so short codes only count when they stand alone in the title (e.g. ".GER." but not "GERM").
    // Two-letter codes normalize structured attributes and profile preferences only. Detect deliberately
    // excludes short title tokens because they collide with ordinary words such as "no" and "it".
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["english"] = "english", ["eng"] = "english",
            ["french"] = "french", ["fre"] = "french", ["fra"] = "french",
            ["truefrench"] = "french", ["vff"] = "french", ["vfq"] = "french",
            ["german"] = "german", ["ger"] = "german", ["deu"] = "german", ["deutsch"] = "german",
            ["spanish"] = "spanish", ["spa"] = "spanish", ["esp"] = "spanish",
            ["castellano"] = "spanish", ["latino"] = "spanish",
            ["italian"] = "italian", ["ita"] = "italian",
            ["portuguese"] = "portuguese", ["por"] = "portuguese", ["dublado"] = "portuguese",
            ["russian"] = "russian", ["rus"] = "russian",
            ["japanese"] = "japanese", ["jpn"] = "japanese", ["jap"] = "japanese",
            ["korean"] = "korean", ["kor"] = "korean",
            ["chinese"] = "chinese", ["chi"] = "chinese", ["zho"] = "chinese",
            ["mandarin"] = "chinese", ["cantonese"] = "chinese",
            ["hindi"] = "hindi", ["hin"] = "hindi",
            ["dutch"] = "dutch", ["nld"] = "dutch", ["flemish"] = "dutch",
            ["polish"] = "polish", ["lektor"] = "polish",
            ["swedish"] = "swedish", ["swe"] = "swedish",
            ["danish"] = "danish", ["dan"] = "danish",
            ["norwegian"] = "norwegian",
            ["finnish"] = "finnish",
            ["arabic"] = "arabic", ["ara"] = "arabic",
            ["turkish"] = "turkish", ["tur"] = "turkish",
            ["czech"] = "czech", ["cze"] = "czech",
            ["hungarian"] = "hungarian", ["hun"] = "hungarian",
            ["en"] = "english", ["fr"] = "french", ["de"] = "german", ["es"] = "spanish",
            ["it"] = "italian", ["pt"] = "portuguese", ["ru"] = "russian", ["ja"] = "japanese",
            ["ko"] = "korean", ["zh"] = "chinese", ["hi"] = "hindi", ["nl"] = "dutch",
            ["pl"] = "polish", ["sv"] = "swedish", ["da"] = "danish", ["no"] = "norwegian",
            ["fi"] = "finnish", ["ar"] = "arabic", ["tr"] = "turkish", ["cs"] = "czech", ["hu"] = "hungarian",
            ["dut"] = "dutch", ["pol"] = "polish", ["nor"] = "norwegian", ["fin"] = "finnish", ["ces"] = "czech",
            [Multi] = Multi, ["multilang"] = Multi, ["multilanguage"] = Multi, ["dual"] = Multi
        };

    [GeneratedRegex(@"[\s._\-()\[\]{}+,]+")]
    private static partial Regex TokenSeparatorRegex();

    /// <summary>
    /// Normalizes a language string (a user preference or an indexer attribute) to its canonical name.
    /// Unknown values pass through lowercased, so an unaliased language still compares by equality.
    /// </summary>
    public static string Canonicalize(string language) {
        var trimmed = language.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// The canonical languages a release declares: the indexer attribute (when present) plus every
    /// language token in the title. Empty means the release is unmarked.
    /// </summary>
    public static IReadOnlySet<string> Detect(string title, string? attributeLanguage) {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(attributeLanguage)) {
            declared.Add(Canonicalize(attributeLanguage));
        }

        foreach (var token in TokenSeparatorRegex().Split(title)) {
            if (token.Length >= 3 && Aliases.TryGetValue(token, out var canonical)) {
                declared.Add(canonical);
            }
        }

        return declared;
    }
}
