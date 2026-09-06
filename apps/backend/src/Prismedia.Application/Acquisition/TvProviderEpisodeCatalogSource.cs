using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Reads bounded episode evidence through enabled metadata plugins without creating library entities.</summary>
public sealed class TvProviderEpisodeCatalogSource(
    IImportTargetIndex targets, IEntityExternalIdentityStore identities, IPluginIdentityRouter router,
    IPluginRequestProgressiveReviewSource reviews, ILogger<TvProviderEpisodeCatalogSource> logger)
    : ITvEpisodeCatalogEvidenceSource {
    private const int MaximumSeasonReads = 6;
    private const int MaximumRoutes = 2;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TvSeasonEpisodeCatalog>> ReadAsync(Guid linkedEntityId, int requestedSeason,
        IReadOnlyList<ImportCandidateFile> files, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (await targets.GetTvSeriesEntityIdAsync(linkedEntityId, cancellationToken) is not { } seriesId) return [];
        var attached = (await identities.ListAsync(seriesId, cancellationToken)).Select(item => item.Identity).Distinct().ToArray();
        if (attached.Length == 0) return [];
        var localSeasons = (await targets.GetSeriesEpisodeCatalogAsync(linkedEntityId, cancellationToken))
            .Where(season => season.SeasonNumber == requestedSeason && season.SeasonEntityId is not null).ToArray();
        if (localSeasons.Length > 1) return [];
        var seasonIdentities = localSeasons.Length == 1
            ? (await identities.ListAsync(localSeasons[0].SeasonEntityId!.Value, cancellationToken)).Select(item => item.Identity).ToArray()
            : [];
        var routes = await router.ResolveAsync(EntityKind.VideoSeries.ToCode(), IdentifyAction.LookupId, attached, cancellationToken);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        foreach (var route in routes.Take(MaximumRoutes)) {
            try {
                var result = await ReadRouteAsync(route, requestedSeason, seasonIdentities, files, budget.Token);
                if (result.Count > 0) return result;
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                logger.LogDebug("TV episode evidence lookup exceeded its time budget.");
                break;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogWarning(ex, "Could not read TV episode evidence from plugin {PluginId}.", route.PluginId);
            }
        }
        return [];
    }

    private async Task<IReadOnlyList<TvSeasonEpisodeCatalog>> ReadRouteAsync(PluginIdentityRoute route, int requestedSeason,
        IReadOnlyList<ExternalIdentity> seasonIdentities, IReadOnlyList<ImportCandidateFile> files, CancellationToken cancellationToken) {
        var request = new RequestReviewRequest(RequestMediaKind.Series, route.PluginId, route.Identity);
        var series = await reviews.StartReviewAsync(request, hideNsfw: false, cancellationToken);
        if (!Matches(series, request, EntityKind.VideoSeries)) return [];
        // A root may carry identities in several ordering systems. A matching series alone does not
        // establish that their season numbers mean the same thing; require the local season identity.
        if (seasonIdentities.Count > 0 && !series!.Proposal.Children.Any(child => child.TargetKind == EntityKind.VideoSeason
            && RequestProposalReading.SeasonNumberOf(child.Patch) == requestedSeason
            && IdentityFor(series, child) is { } identity && seasonIdentities.Contains(identity))) return [];
        var declared = files.Select(file => TvReleaseTokens.ParseEpisodes(Path.GetFileNameWithoutExtension(file.RelativePath))?.Season)
            .OfType<int>().ToHashSet();
        var seasons = series!.Proposal.Children.Where(child => child.TargetKind == EntityKind.VideoSeason)
            .Select(child => (Proposal: child, Number: RequestProposalReading.SeasonNumberOf(child.Patch),
                Identity: IdentityFor(series, child)))
            .Where(item => item.Number is >= 0 && item.Identity is not null)
            .GroupBy(item => item.Number).Where(group => group.Count() == 1).Select(group => group.Single())
            .OrderBy(item => item.Number == requestedSeason ? 0 : declared.Contains(item.Number!.Value) ? 1 : 2)
            .ThenBy(item => Math.Abs((long)item.Number!.Value - requestedSeason)).ThenBy(item => item.Number)
            .Take(MaximumSeasonReads);
        var result = new List<TvSeasonEpisodeCatalog>();
        foreach (var season in seasons) {
            cancellationToken.ThrowIfCancellationRequested();
            var seasonRequest = new RequestReviewRequest(RequestMediaKind.Season, route.PluginId, season.Identity!);
            var review = await reviews.StartReviewAsync(seasonRequest, hideNsfw: false, cancellationToken);
            if (!Matches(review, seasonRequest, EntityKind.VideoSeason)
                || RequestProposalReading.SeasonNumberOf(review!.Proposal.Patch) != season.Number) continue;
            var episodes = ReadEpisodes(review, season.Number!.Value);
            if (episodes.Count > 0) result.Add(new(null, season.Number.Value, episodes) { ProviderIdentity = season.Identity });
        }
        return result;
    }

    private static IReadOnlyList<TvEpisodeTitle> ReadEpisodes(RequestReviewResponse season, int seasonNumber) =>
        season.Proposal.Children.Where(child => child.TargetKind == EntityKind.VideoEpisode
                && !string.IsNullOrWhiteSpace(child.Patch.Title)
                && (RequestProposalReading.SeasonNumberOf(child.Patch) is not { } parent || parent == seasonNumber))
            .Select(child => (Proposal: child, Number: RequestProposalReading.EpisodeNumberOf(child.Patch), Identity: IdentityFor(season, child)))
            .Where(item => item.Number is > 0 && item.Identity is not null)
            .GroupBy(item => item.Number).Where(group => group.Count() == 1).Select(group => group.Single())
            .GroupBy(item => item.Identity).Where(group => group.Count() == 1).Select(group => group.Single())
            .OrderBy(item => item.Number)
            .Select(item => new TvEpisodeTitle(item.Number!.Value, item.Proposal.Patch.Title!, AbsoluteEpisode:
                item.Proposal.Patch.Positions.TryGetValue(EntityPositionCodes.AbsoluteEpisode, out var absolute) && absolute > 0 ? absolute : null) {
                ProviderIdentity = item.Identity
            }).ToArray();

    private static bool Matches(RequestReviewResponse? response, RequestReviewRequest request, EntityKind kind) =>
        response is not null && response.PluginId == request.PluginId && response.ExternalIdentity == request.ExternalIdentity
        && response.Kind == request.Kind && response.EntityKind == kind && response.Proposal.TargetKind == kind
        && IdentityFor(response, response.Proposal) == request.ExternalIdentity;

    private static ExternalIdentity? IdentityFor(RequestReviewResponse review, EntityMetadataProposal proposal) {
        var matches = review.Targets.Where(target => target.ProposalId == proposal.ProposalId && target.EntityKind == proposal.TargetKind).ToArray();
        if (matches.Length != 1) return null;
        var identity = matches[0].ExternalIdentity;
        return proposal.Patch.ExternalIds.TryGetValue(identity.Namespace, out var value) && value == identity.Value ? identity : null;
    }
}
