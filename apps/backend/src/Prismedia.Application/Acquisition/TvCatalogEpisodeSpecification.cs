using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Rejects an episode release whose complete title confidently identifies another known episode.</summary>
internal sealed class TvCatalogEpisodeSpecification : IReleaseSpecification {
    private readonly (int Season, TvEpisodeTitle Episode)[] _episodes;
    private readonly TvEpisodeEvidenceIndex _titles;

    public TvCatalogEpisodeSpecification(IReadOnlyList<TvSeasonEpisodeCatalog> catalog) {
        _episodes = catalog.SelectMany(season => season.Episodes
            .Where(episode => (episode.EntityId is not null || episode.ProviderIdentity is not null)
                && TvCrossSeasonImportEvidence.IsDistinctiveTitle(episode.Title))
            .Select(episode => (season.SeasonNumber, episode))).ToArray();
        // Assign unique internal slots so repeated episode numbers across seasons cannot collapse.
        _titles = new(_episodes.Select((entry, index) => entry.Episode with { Episode = index + 1 }).ToArray());
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
        var matched = _titles.Match(tail, titlesOnly: true);
        // Missing titles, translations, repeated titles and overlapping fragments cannot disprove numbering.
        if (matched.Length == 0 || !_titles.HasDistinctLeadingTitles(tail, matched)) return null;
        return matched.Any(slot => _episodes[slot - 1].Season == season && _episodes[slot - 1].Episode.Episode == episode)
            ? null : Reason;
    }
}
