namespace Prismedia.Application.Acquisition;

/// <summary>Reuses one catalog's normalized evidence across every file and alignment pass in a TV plan.</summary>
internal sealed class TvEpisodeEvidenceIndex {
    private readonly Dictionary<string, List<TitleEntry>> _titles = new(StringComparer.Ordinal);
    private readonly Dictionary<int, HashSet<int>> _absolute = new();
    private sealed record TitleEntry(int Episode, IReadOnlyList<string> Tokens);
    public int Count { get; }

    /// <summary>Indexes usable provider titles and verified absolute positions without selecting an episode.</summary>
    public TvEpisodeEvidenceIndex(IReadOnlyList<TvEpisodeTitle> episodes) {
        Count = episodes.Count;
        foreach (var episode in episodes) {
            var identity = TvEpisodeIdentifiers.Create(episode.Title, episode.AbsoluteEpisode);
            var tokens = ReleaseTitleIdentity.ComparableTokens(identity.ProviderTitle);
            if (tokens.Count > 0) {
                if (!_titles.TryGetValue(tokens[0], out var titles)) _titles[tokens[0]] = titles = [];
                titles.Add(new(episode.Episode, tokens));
            }
            if (episode.AbsoluteEpisode is > 0) {
                if (!_absolute.TryGetValue(episode.AbsoluteEpisode.Value, out var slots))
                    _absolute[episode.AbsoluteEpisode.Value] = slots = [];
                slots.Add(episode.Episode);
            }
        }
    }

    /// <summary>Returns every matching catalog slot; multiple identities remain ambiguous to the caller.</summary>
    public int[] Match(string candidate, bool titlesOnly = false) {
        var matches = new HashSet<int>();
        var tokens = ReleaseTitleIdentity.ComparableTokens(candidate);
        for (var start = 0; start < tokens.Count; start++) {
            if (!_titles.TryGetValue(tokens[start], out var titles)) continue;
            foreach (var title in titles) {
                if (title.Tokens.Count > tokens.Count - start) continue;
                var matched = true;
                for (var offset = 1; offset < title.Tokens.Count; offset++) {
                    if (tokens[start + offset] == title.Tokens[offset]) continue;
                    matched = false;
                    break;
                }
                if (matched) matches.Add(title.Episode);
            }
        }
        if (!titlesOnly && _absolute.Count > 0) {
            foreach (var number in TvAbsoluteEpisodeTokens.ReadNumbers(candidate)) {
                if (_absolute.TryGetValue(number, out var slots)) matches.UnionWith(slots);
            }
        }
        return matches.Order().ToArray();
    }
}
