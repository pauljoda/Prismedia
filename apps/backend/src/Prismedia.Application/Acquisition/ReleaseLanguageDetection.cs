using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Detects the language(s) a release declares, from the indexer's language attribute and from language
/// tokens in the release title (FRENCH, GERMAN, ITA, MULTi, …). Canonical names are lowercase English
/// language names; user-entered preferred languages are normalized through the same alias table so
/// "eng", "English", and an indexer's "ENGLISH" all compare equal. Subtitle declarations are excluded.
/// Unmarked and multi-audio releases remain uncertain; neither confirms a particular audio language.
/// </summary>
public static partial class ReleaseLanguageDetection {
    /// <summary>Canonical marker for unspecified multiple audio languages; allows fallback without confirming a language.</summary>
    public const string Multi = "multi";

    // Alias token → canonical language name. Tokens are matched whole between separator characters,
    // so short codes only count when they stand alone in the title (e.g. ".GER." but not "GERM").
    // Two-letter codes normalize structured attributes and profile preferences only. Detect deliberately
    // excludes short title tokens because they collide with ordinary words such as "no" and "it".
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["english"] = "english", ["eng"] = "english",
            ["en-us"] = "english", ["en-gb"] = "english",
            ["french"] = "french", ["fre"] = "french", ["fra"] = "french",
            ["fr-fr"] = "french", ["fr-ca"] = "french",
            ["truefrench"] = "french", ["vff"] = "french", ["vfq"] = "french",
            ["german"] = "german", ["ger"] = "german", ["deu"] = "german", ["deutsch"] = "german",
            ["spanish"] = "spanish", ["spa"] = "spanish", ["esp"] = "spanish",
            ["es-es"] = "spanish", ["es-mx"] = "spanish", ["es-419"] = "spanish",
            ["castellano"] = "spanish", ["latino"] = "spanish",
            ["italian"] = "italian", ["ita"] = "italian",
            ["portuguese"] = "portuguese", ["por"] = "portuguese", ["dublado"] = "portuguese",
            ["pt-br"] = "portuguese", ["ptbr"] = "portuguese", ["pt-pt"] = "portuguese",
            ["russian"] = "russian", ["rus"] = "russian",
            ["japanese"] = "japanese", ["jpn"] = "japanese", ["jap"] = "japanese",
            ["korean"] = "korean", ["kor"] = "korean",
            ["chinese"] = "chinese", ["chi"] = "chinese", ["zho"] = "chinese",
            ["zh-cn"] = "chinese", ["zh-tw"] = "chinese",
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
            [Multi] = Multi, ["multilang"] = Multi, ["multilanguage"] = Multi
        };

    /// <summary>Canonical audio languages available to rule editors, excluding ambiguous multi-audio.</summary>
    public static IReadOnlyList<string> KnownLanguages => Aliases.Values.Where(value => value != Multi).Distinct().Order().ToArray();

    [GeneratedRegex(@"[\s._\-()\[\]{}+,/;|]+")]
    private static partial Regex TokenSeparatorRegex();

    // Subtitle labels qualify the adjacent language list, not the audio. Brackets delimit lists so
    // a subtitle clause cannot consume an independent audio declaration outside it.
    private const string LanguageSeparator = @"[\s._+,/;\-]+";
    private const string SubtitleMarker = @"(?:subs?|subtitles?|subbed)";
    private static readonly string LanguageTokenPattern = @"(?:" + string.Join("|", Aliases.Keys.Select(AliasPattern)) + ")";
    // An explicit regional tag is stronger than an isolated two-letter word. Keep it intact before
    // ordinary release separators split it, and use the same vocabulary when excluding subtitles.
    private static readonly Regex RegionalLanguageTokens = new(
        @"(?<![\p{L}\p{N}])(?:" + string.Join("|", Aliases.Keys.Where(alias => alias.Contains('-')).Select(AliasPattern))
        + @")(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static string AliasPattern(string alias) => string.Join("[-_]", alias.Split('-').Select(Regex.Escape));
    private static readonly Regex SubtitleSuffix = new(
        @"(?<![\p{L}\p{N}])" + LanguageTokenPattern + "(?:" + LanguageSeparator + LanguageTokenPattern + ")*"
        + @"[\s._+,/;\-]*" + SubtitleMarker + @"(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SubtitlePrefix = new(
        @"(?<![\p{L}\p{N}])" + SubtitleMarker + LanguageSeparator + LanguageTokenPattern
        + "(?:" + LanguageSeparator + LanguageTokenPattern + @")*(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    [GeneratedRegex(@"\bmulti[\s._-]*(?:subs?|subtitles?|subbed)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiSubtitleRegex();

    [GeneratedRegex(@"\bdual[\s._-]+audio\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DualAudioRegex();

    [GeneratedRegex(@"[\[(](?<languages>[a-z]{2}(?:\s*[+/,]\s*[a-z]{2})+)[\])]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitShortLanguageListRegex();

    /// <summary>
    /// Returns the strongest preferred audio declaration, in preference order. Explicit matches rank
    /// above unspecified multi-audio, then unmarked releases. Zero also covers unpreferred languages;
    /// the acceptance gate decides whether those releases are eligible at all.
    /// </summary>
    public static int PreferenceRank(string title, string? attributeLanguage, IReadOnlyList<string> preferred) {
        if (preferred.Count == 0) return 0;
        var declared = Detect(title, attributeLanguage);
        for (var i = 0; i < preferred.Count; i++) {
            if (declared.Contains(Canonicalize(preferred[i]))) return preferred.Count - i + 1;
        }
        return declared.Contains(Multi) ? 1 : 0;
    }

    /// <summary>Ranks audio declarations after excluding known work and episode title words.</summary>
    public static int PreferenceRank(IndexerRelease release, BookAcquisitionRules rules) =>
        PreferenceRank(AudioEvidence(release.Title, rules), release.Language, rules.PreferredLanguages);

    /// <summary>
    /// Encodes language precedence in the durable score, so search, persisted picks and retries agree.
    /// Other ranking signals occupy a bounded band; ordinary profile scores remain unchanged within it.
    /// Clearing preferred languages leaves all ordering to the user's weighted rules.
    /// </summary>
    public static double RankScore(IndexerRelease release, BookAcquisitionRules rules, double score) {
        if (rules.PreferredLanguages.Count == 0) return score;
        const double bandWidth = 1_000_000_000_000;
        const double secondaryLimit = bandWidth / 4;
        return PreferenceRank(release, rules) * bandWidth
            + Math.Clamp(score, -secondaryLimit, secondaryLimit);
    }

    /// <summary>
    /// Normalizes a language string (a user preference or an indexer attribute) to its canonical name.
    /// Unknown values pass through lowercased, so an unaliased language still compares by equality.
    /// </summary>
    public static string Canonicalize(string language) {
        var trimmed = language.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical)
            || Aliases.TryGetValue(trimmed.Replace('_', '-'), out canonical)
            ? canonical : trimmed.ToLowerInvariant();
    }

    /// <summary>Reads audio declarations while preserving title words for separate identity and regex rules.</summary>
    public static IReadOnlySet<string> Detect(string title, string? attributeLanguage, BookAcquisitionRules rules) =>
        Detect(AudioEvidence(title, rules), attributeLanguage);

    private static string AudioEvidence(string title, BookAcquisitionRules rules) {
        var evidence = AcquisitionWorkTitles.EpisodeEvidence(title, rules.TargetTitle, rules.TargetAlternativeTitles);
        if (!string.IsNullOrWhiteSpace(rules.TargetEpisodeTitle)
            && TvReleaseTokens.ParseEpisodes(evidence) is { } unit
            && unit.Season == rules.SeasonNumber && unit.Episodes.Count == 1
            && unit.Episodes[0] == rules.EpisodeNumber
            && TvReleaseTokens.EpisodeTitleTail(evidence) is { } tail) {
            var suffix = ReleaseTitleIdentity.WithoutLeadingWorkTitle(tail, rules.TargetEpisodeTitle);
            if (suffix.Length < tail.Length) evidence = evidence[..^tail.Length] + suffix;
        }
        return evidence;
    }

    /// <summary>
    /// The canonical languages a release declares: the indexer attribute (when present) plus every
    /// language token in the title. Empty means the release is unmarked.
    /// </summary>
    public static IReadOnlySet<string> Detect(string title, string? attributeLanguage) {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(attributeLanguage)) {
            foreach (var attribute in attributeLanguage.Split([',', '/', ';', '|', '+'], StringSplitOptions.RemoveEmptyEntries)) {
                declared.Add(Canonicalize(attribute));
            }
        }

        var audioTitle = SubtitlePrefix.Replace(SubtitleSuffix.Replace(MultiSubtitleRegex().Replace(title, " "), " "), " ");
        if (DualAudioRegex().IsMatch(audioTitle)) declared.Add(Multi);
        foreach (Match match in RegionalLanguageTokens.Matches(audioTitle)) declared.Add(Canonicalize(match.Value));
        foreach (Match match in ExplicitShortLanguageListRegex().Matches(audioTitle)) {
            var tokens = TokenSeparatorRegex().Split(match.Groups["languages"].Value);
            if (tokens.All(Aliases.ContainsKey)) {
                foreach (var token in tokens) declared.Add(Canonicalize(token));
            }
        }
        foreach (var token in TokenSeparatorRegex().Split(audioTitle)) {
            if (token.Length >= 3 && Aliases.TryGetValue(token, out var canonical)) {
                declared.Add(canonical);
            }
        }

        return declared;
    }
}
