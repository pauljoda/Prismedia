using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Rejects an episode release whose complete title confidently identifies another known episode.</summary>
internal sealed class TvCatalogEpisodeSpecification : IReleaseSpecification {
    private readonly (int Season, TvEpisodeTitle Episode)[] _episodes;
    private readonly TvEpisodeEvidenceIndex _titles;

    public TvCatalogEpisodeSpecification(IReadOnlyList<TvSeasonEpisodeCatalog> catalog) {
        _episodes = catalog.SelectMany(season => season.Episodes
            .Where(episode => TvCrossSeasonImportEvidence.IsDistinctiveTitle(episode.Title))
            .Select(episode => (season.SeasonNumber, episode))).ToArray();
        // Assign unique internal slots so repeated episode numbers across seasons cannot collapse.
        _titles = new(_episodes.Select((entry, index) => entry.Episode with { Episode = index + 1 }).ToArray());
    }

    /// <summary>Allows alternate numbering only when complete, unique catalog titles prove every declared half.</summary>
    public bool IdentifiesRequestedEpisode(IndexerRelease release, BookAcquisitionRules rules) {
        var declared = TvReleaseTokens.ParseEpisodes(release.Title);
        if (declared is null || declared.Value.Season != rules.SeasonNumber || rules.EpisodeNumber is null) return false;
        var tail = TvReleaseTokens.EpisodeTitleTail(release.Title) ?? string.Empty;
        var matched = _titles.Match(tail, titlesOnly: true);
        return matched.Length >= declared.Value.Episodes.Count
            && _titles.HasDistinctLeadingTitles(tail, matched)
            && matched.All(slot => _episodes[slot - 1].Season == rules.SeasonNumber
                && _episodes[slot - 1].Episode is { EntityId: not null } or { ProviderIdentity: not null })
            && matched.Any(slot => _episodes[slot - 1].Episode.Episode == rules.EpisodeNumber);
    }

    public ReleaseRejectionReason Reason => ReleaseRejectionReason.WrongTvUnit;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.SeasonNumber is not { } season || rules.EpisodeNumber is not { } episode) return null;
        var declared = TvReleaseTokens.ParseEpisodes(release.Title);
        // A multi-episode file may name only one half. Its import plan must resolve complete coverage.
        if (declared?.Episodes.Count > 1) return null;
        var tail = declared is null
            ? AcquisitionWorkTitles.EpisodeEvidence(release.Title, rules.TargetTitle, rules.TargetAlternativeTitles)
            : TvReleaseTokens.EpisodeTitleTail(release.Title) ?? string.Empty;
        var matched = _titles.Match(tail, titlesOnly: true)
            .Where(slot => _episodes[slot - 1].Episode is { EntityId: not null } or { ProviderIdentity: not null }).ToArray();
        // Repeated airings of the same complete title can still disprove a differently named target.
        // Preserve every matching identity for the target check, collapsing only the title-proof slots.
        var distinctTitles = matched.GroupBy(slot => string.Join(' ',
                ReleaseTitleIdentity.ComparableTokens(_episodes[slot - 1].Episode.Title)))
            .Select(group => group.First()).ToArray();
        if (matched.Length == 0 || !_titles.HasDistinctLeadingTitles(tail, distinctTitles)) return null;
        return matched.Any(slot => _episodes[slot - 1].Season == season && _episodes[slot - 1].Episode.Episode == episode)
            ? null : Reason;
    }
}
