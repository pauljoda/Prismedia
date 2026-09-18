using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Saves an explicitly reviewed finite episode selection as a stable wanted series tree before a
/// separate external-manager decision. This path never starts native acquisition or monitoring.
/// </summary>
public sealed class ReviewedWantedSeriesService(
    IWantedEntityWriter wanted,
    IWantedSuppressionStore suppressions,
    IPluginIdentityRouter routes,
    IEntityLifecycleMutationLease lifecycle) {
    /// <summary>
    /// Validates every selected episode and exact plugin route before materializing the series, its
    /// selected seasons, and its selected episodes.
    /// </summary>
    public async Task<PreparedWantedSeriesResponse> PrepareAsync(
        ReviewedRequestCommitRequest request,
        CancellationToken token) {
        ValidateEnvelope(request);
        var review = ReviewedRequestProposalValidator.Validate(request, request.Review!, request.Proposal!);
        var selection = ReadSelection(request, review);
        await RequireExactRoutesAsync(request.PluginId, selection, token);

        var series = await wanted.EnsureAsync(
            EntityKind.VideoSeries,
            selection.Series.Identity,
            selection.Series.Title,
            parentEntityId: null,
            matchTitleKindWide: true,
            token);

        var seasons = await wanted.EnsureChildrenAsync(
            series.EntityId,
            selection.Seasons.Select(season => new WantedEntityEnsureRequest(
                EntityKind.VideoSeason,
                season.Identity,
                season.Title,
                PreferredEntityId: season.Proposal.TargetEntityId)).ToArray(),
            token);
        var seasonsByProposalId = selection.Seasons
            .Select((season, index) => (season.Proposal.ProposalId, Entity: seasons[index]))
            .ToDictionary(value => value.ProposalId, value => value.Entity, StringComparer.Ordinal);

        var materializedEpisodes = new List<(EpisodeSelection Selection, WantedEntityResult Entity, Guid? SeasonEntityId)>();
        foreach (var parent in selection.Episodes.GroupBy(episode => episode.Season?.Proposal.ProposalId)) {
            var seasonEntityId = parent.Key is null
                ? (Guid?)null
                : seasonsByProposalId[parent.Key].EntityId;
            var episodeParentId = seasonEntityId ?? series.EntityId;
            var episodes = parent.ToArray();
            var entities = await wanted.EnsureChildrenAsync(
                episodeParentId,
                episodes.Select(episode => new WantedEntityEnsureRequest(
                    EntityKind.VideoEpisode,
                    episode.Identity,
                    episode.Title,
                    PreferredEntityId: episode.Proposal.TargetEntityId)).ToArray(),
                token);
            materializedEpisodes.AddRange(episodes.Select((episode, index) =>
                (episode, entities[index], seasonEntityId)));
        }

        var routesByEntity = new Dictionary<Guid, PluginIdentityRoute> {
            [series.EntityId] = new(request.PluginId, selection.Series.Identity)
        };
        foreach (var (season, index) in selection.Seasons.Select((value, index) => (value, index))) {
            if (!routesByEntity.TryAdd(seasons[index].EntityId, new(request.PluginId, season.Identity))) {
                throw Invalid("Selected TV targets resolved to the same local entity.");
            }
        }
        foreach (var episode in materializedEpisodes) {
            if (!routesByEntity.TryAdd(episode.Entity.EntityId, new(request.PluginId, episode.Selection.Identity))) {
                throw Invalid("Selected TV targets resolved to the same local entity.");
            }
        }

        var entityIds = routesByEntity.Keys.ToArray();
        if (!await lifecycle.ExecuteManyAsync(entityIds, async ct => {
            var bound = await wanted.BindProviderIdentitiesAsync(routesByEntity, ct);
            if (!bound.SetEquals(entityIds)) {
                throw Invalid("The exact metadata plugin route changed before saving. Review this series again.");
            }

            if (!series.HasFile) {
                await wanted.ApplyProposalWithDeferredArtworkAsync(
                    series.EntityId,
                    ForEntity(selection.Series.Proposal, series.EntityId),
                    ct);
            }
            for (var index = 0; index < selection.Seasons.Count; index++) {
                if (!seasons[index].HasFile) {
                    await wanted.ApplyProposalWithDeferredArtworkAsync(
                        seasons[index].EntityId,
                        ForEntity(selection.Seasons[index].Proposal, seasons[index].EntityId),
                        ct);
                }
            }
            foreach (var episode in materializedEpisodes.Where(item => !item.Entity.HasFile)) {
                await wanted.ApplyProposalWithDeferredArtworkAsync(
                    episode.Entity.EntityId,
                    ForEntity(episode.Selection.Proposal, episode.Entity.EntityId),
                    ct);
            }

            await suppressions.ClearAsync(
                selection.AllIdentities,
                ct);
        }, token)) {
            throw new EntityLifecycleMutationConflictException(series.EntityId);
        }

        return new PreparedWantedSeriesResponse(
            series.EntityId,
            selection.Series.Title,
            materializedEpisodes.Select(item => new PreparedWantedEpisode(
                item.Entity.EntityId,
                item.SeasonEntityId,
                item.Selection.Title,
                item.Selection.SeasonNumber,
                item.Selection.EpisodeNumber,
                item.Selection.AbsoluteNumber,
                item.Entity.HasFile,
                item.Selection.Identity)).ToArray());
    }

    /// <summary>Validates a finite reviewed episode selection and derives exact Sonarr lookup evidence without writing.</summary>
    internal async Task<ReviewedWantedPlan> ReviewForManagerAsync(
        ReviewedRequestCommitRequest request,
        CancellationToken token) {
        ValidateEnvelope(request);
        var review = ReviewedRequestProposalValidator.Validate(request, request.Review!, request.Proposal!);
        var selection = ReadSelection(request, review);
        await RequireExactRoutesAsync(request.PluginId, selection, token);
        var rootIds = selection.Series.Proposal.Patch.ExternalIds
            .Where(pair => pair.Key is ExternalIdProviders.Tvdb or ExternalIdProviders.Tmdb)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (selection.Series.Identity.Namespace is ExternalIdProviders.Tvdb or ExternalIdProviders.Tmdb)
            rootIds[selection.Series.Identity.Namespace] = selection.Series.Identity.Value;
        if (rootIds.Count == 0)
            throw Invalid("Identify the series with a TVDB or TMDB identity before choosing external fulfillment.");
        var targets = selection.Episodes.Select(episode => {
            var ids = episode.Proposal.Patch.ExternalIds
                .Where(pair => pair.Key == ExternalIdProviders.Tvdb)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (episode.Identity.Namespace == ExternalIdProviders.Tvdb)
                ids[episode.Identity.Namespace] = episode.Identity.Value;
            if (ids.Count == 0)
                throw Invalid("Every selected episode needs an exact TVDB identity for external fulfillment.");
            return new ManagedLookupTarget(
                EntityKind.VideoEpisode,
                ids,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                episode.AbsoluteNumber);
        }).ToArray();
        return new(selection.Series.Title, new(EntityKind.VideoSeries, rootIds, targets));
    }

    private static void ValidateEnvelope(ReviewedRequestCommitRequest request) {
        if (request.Kind != RequestMediaKind.Series
            || string.IsNullOrWhiteSpace(request.PluginId)
            || request.Review is not {
                Kind: RequestMediaKind.Series,
                EntityKind: EntityKind.VideoSeries,
                Proposal.TargetKind: EntityKind.VideoSeries
            }
            || request.Proposal is not { TargetKind: EntityKind.VideoSeries }
            || request.SelectedProposalIds is null
            || request.SelectedProposalIds.Count == 0
            || request.SelectedProposalIds.Any(string.IsNullOrWhiteSpace)
            || request.SelectedProposalIds.Distinct(StringComparer.Ordinal).Count()
                != request.SelectedProposalIds.Count
            || !string.Equals(request.ProposalRevision, request.Review.Revision, StringComparison.Ordinal)
            || !string.Equals(
                RequestProposalRevision.Compute(request.Review.Proposal),
                request.Review.Revision,
                StringComparison.Ordinal)) {
            throw Invalid(
                "Submit a complete reviewed series and a non-empty finite episode selection before choosing external fulfillment.");
        }
    }

    private static SeriesSelection ReadSelection(
        ReviewedRequestCommitRequest request,
        RequestReviewResponse review) {
        var targets = new Dictionary<string, RequestReviewTarget>(StringComparer.Ordinal);
        foreach (var target in review.Targets) {
            if (string.IsNullOrWhiteSpace(target.ProposalId)
                || !targets.TryAdd(target.ProposalId, target)) {
                throw Invalid("The reviewed series contains duplicate or empty proposal ids.");
            }
        }

        var series = ReadNode(
            review.Proposal,
            RequestMediaKind.Series,
            EntityKind.VideoSeries,
            targets);
        if (series.Identity != review.ExternalIdentity) {
            throw Invalid("The reviewed series root identity does not match the request.");
        }

        var selectedIds = request.SelectedProposalIds.ToHashSet(StringComparer.Ordinal);
        var seasons = new List<SeasonSelection>();
        var episodes = new List<EpisodeSelection>();
        foreach (var child in review.Proposal.Children.Where(node => !node.TargetKind.IsRelationship())) {
            if (child.TargetKind == EntityKind.VideoEpisode) {
                if (selectedIds.Contains(child.ProposalId)) {
                    episodes.Add(ReadEpisode(child, season: null, targets));
                }
                continue;
            }
            if (child.TargetKind != EntityKind.VideoSeason) {
                throw Invalid("The reviewed series contains an unsupported structural target.");
            }

            var seasonNode = ReadNode(child, RequestMediaKind.Season, EntityKind.VideoSeason, targets);
            var seasonNumber = RequestProposalReading.SeasonNumberOf(child.Patch);
            if (seasonNumber is null or < 0) {
                throw Invalid("Every selected episode must belong to a season with a non-negative number.");
            }
            var season = new SeasonSelection(child, seasonNode.Identity, seasonNode.Title, seasonNumber.Value);
            var selectedInSeason = child.Children
                .Where(node => !node.TargetKind.IsRelationship() && selectedIds.Contains(node.ProposalId))
                .ToArray();
            if (selectedInSeason.Length == 0) {
                continue;
            }
            if (selectedInSeason.Any(node => node.TargetKind != EntityKind.VideoEpisode)) {
                throw Invalid("A finite TV selection can contain only episodes.");
            }
            seasons.Add(season);
            episodes.AddRange(selectedInSeason.Select(episode => ReadEpisode(episode, season, targets)));
        }

        if (episodes.Count != selectedIds.Count) {
            throw Invalid("Every selected proposal must be a reviewed episode in the submitted series tree.");
        }
        if (episodes.Select(episode => episode.Identity).Distinct().Count() != episodes.Count) {
            throw Invalid("Selected episodes must have unique external identities.");
        }
        if (episodes.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber)).Distinct().Count()
            != episodes.Count) {
            throw Invalid("Selected episodes must have unique season and episode numbers.");
        }

        return new SeriesSelection(series, seasons, episodes);
    }

    private static EpisodeSelection ReadEpisode(
        EntityMetadataProposal proposal,
        SeasonSelection? season,
        IReadOnlyDictionary<string, RequestReviewTarget> targets) {
        var node = ReadNode(proposal, RequestMediaKind.Episode, EntityKind.VideoEpisode, targets);
        var declaredSeason = RequestProposalReading.SeasonNumberOf(proposal.Patch);
        var seasonNumber = season?.SeasonNumber ?? declaredSeason;
        if (seasonNumber is null or < 0
            || season is not null && declaredSeason is not null && declaredSeason != season.SeasonNumber) {
            throw Invalid("Every selected episode must have one unambiguous non-negative season number.");
        }
        var episodeNumber = RequestProposalReading.EpisodeNumberOf(proposal.Patch);
        if (episodeNumber is null or <= 0) {
            throw Invalid("Every selected episode must have a positive episode number.");
        }
        int? absoluteNumber = proposal.Patch.Positions.TryGetValue(
            EntityPositionCodes.AbsoluteEpisode,
            out var declaredAbsolute)
            ? declaredAbsolute
            : null;
        if (absoluteNumber <= 0) {
            absoluteNumber = null;
        }
        return new EpisodeSelection(
            proposal,
            node.Identity,
            node.Title,
            season,
            seasonNumber.Value,
            episodeNumber.Value,
            absoluteNumber);
    }

    private static ReviewedNode ReadNode(
        EntityMetadataProposal proposal,
        RequestMediaKind kind,
        EntityKind entityKind,
        IReadOnlyDictionary<string, RequestReviewTarget> targets) {
        if (proposal.TargetKind != entityKind
            || !targets.TryGetValue(proposal.ProposalId, out var target)
            || target.Kind != kind
            || target.EntityKind != entityKind
            || !target.Requestable
            || !proposal.Patch.ExternalIds.TryGetValue(target.ExternalIdentity.Namespace, out var value)
            || !string.Equals(value, target.ExternalIdentity.Value, StringComparison.Ordinal)) {
            throw Invalid("Every selected TV target needs one matching requestable external identity.");
        }
        var title = proposal.Patch.Title;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512) {
            throw Invalid("Every selected TV target needs a valid title.");
        }
        return new ReviewedNode(proposal, target.ExternalIdentity, title.Trim());
    }

    private async Task RequireExactRoutesAsync(
        string pluginId,
        SeriesSelection selection,
        CancellationToken token) {
        await RequireExactRoutesAsync(
            pluginId,
            EntityKind.VideoSeries,
            [selection.Series.Identity],
            token);
        await RequireExactRoutesAsync(
            pluginId,
            EntityKind.VideoSeason,
            selection.Seasons.Select(season => season.Identity).ToArray(),
            token);
        await RequireExactRoutesAsync(
            pluginId,
            EntityKind.VideoEpisode,
            selection.Episodes.Select(episode => episode.Identity).ToArray(),
            token);
    }

    private async Task RequireExactRoutesAsync(
        string pluginId,
        EntityKind kind,
        IReadOnlyList<ExternalIdentity> identities,
        CancellationToken token) {
        if (identities.Count == 0) {
            return;
        }
        var resolved = await routes.ResolveAsync(kind.ToCode(), IdentifyAction.LookupId, identities, token);
        foreach (var identity in identities) {
            if (resolved.Count(route =>
                    route.Identity == identity
                    && string.Equals(route.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)) != 1) {
                throw Invalid(
                    "The exact metadata plugin route is unavailable for every selected TV target. Review this series again through an enabled plugin.");
            }
        }
    }

    private static EntityMetadataProposal ForEntity(EntityMetadataProposal proposal, Guid entityId) =>
        proposal with {
            TargetEntityId = entityId,
            Children = proposal.Children.Where(child => child.TargetKind.IsRelationship()).ToArray()
        };

    private static RequestCommitValidationException Invalid(string message) => new(message);

    private sealed record ReviewedNode(EntityMetadataProposal Proposal, ExternalIdentity Identity, string Title);

    private sealed record SeasonSelection(
        EntityMetadataProposal Proposal,
        ExternalIdentity Identity,
        string Title,
        int SeasonNumber);

    private sealed record EpisodeSelection(
        EntityMetadataProposal Proposal,
        ExternalIdentity Identity,
        string Title,
        SeasonSelection? Season,
        int SeasonNumber,
        int EpisodeNumber,
        int? AbsoluteNumber);

    private sealed record SeriesSelection(
        ReviewedNode Series,
        IReadOnlyList<SeasonSelection> Seasons,
        IReadOnlyList<EpisodeSelection> Episodes) {
        public IReadOnlyList<ExternalIdentity> AllIdentities { get; } =
            [Series.Identity, .. Seasons.Select(season => season.Identity), .. Episodes.Select(episode => episode.Identity)];
    }
}
