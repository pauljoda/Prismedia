using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Requires an alternative release to improve the retained attempt under current import evidence and profile rules.</summary>
public sealed class HeldAcquisitionCandidatePolicy(IAcquisitionStore acquisitions, IImportTargetIndex targets,
    IDownloadPayloadReader payloads, IBookAcquisitionProfileStore profiles, SettingsService settings,
    IAcquisitionReleaseInventory? inventory = null) {
    /// <summary>Filters recovery candidates without changing the held acquisition or any files.</summary>
    public async Task<IReadOnlyList<ScoredRelease>> FilterAsync(AcquisitionSearchInput input,
        IReadOnlyList<ScoredRelease> candidates, CancellationToken cancellationToken) {
        if (input.RecoveryOfAcquisitionId is not { } heldId) return candidates;
        var held = await acquisitions.GetImportContextAsync(heldId, cancellationToken);
        var selected = await acquisitions.GetSelectedReleaseAsync(heldId, cancellationToken);
        if (held is null || selected is null || selected.ManualPick || held.ImportManualReview
            || held.EntityId != input.EntityId || held.Kind != input.Kind || held.HasImportRecoveryState
            || held.FinalSourcePath is not null || held.ContentPath is null
            || await acquisitions.GetStatusAsync(heldId, cancellationToken) != AcquisitionStatus.ManualImportRequired)
            return candidates.Select(Reject).ToArray();
        var retained = payloads.Read(held.ContentPath);
        // Lost download storage is itself a gap; it must not permanently disable reacquisition.
        var previousReleases = await ReadPreviousReleasesAsync(heldId, retained is not null, cancellationToken);
        if (previousReleases is null) return candidates.Select(Reject).ToArray();
        var profile = await profiles.GetImportProfileAsync(input.ProfileId, input.Kind, cancellationToken);
        var rules = (await profiles.GetRulesAsync(input.ProfileId, input.Kind, cancellationToken)) with {
            IsUpgradeSearch = true,
            OwnedMediaQuality = MediaQualityLadder.Detect(input.Kind, selected.Title).Code,
            OwnedMediaRevision = ReleaseRevisionDetection.Detect(selected.Title),
            ProperPolicy = (await settings.GetProperDownloadSettingsAsync(cancellationToken)).Policy
        };
        rules = rules with { OwnedFormatScore = CustomFormatEvaluation.Score(selected.Title, rules) };
        var usable = HeldAcquisitionRecoveryPolicy.UsablePayload(retained,
            (await acquisitions.GetTransferInfoAsync(heldId, cancellationToken))?.ImportResult);
        var baseline = usable is null ? [] : await CoverageAsync(input, held, usable, profile, cancellationToken);
        var result = new List<ScoredRelease>(candidates.Count);
        foreach (var candidate in candidates) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidate.Accepted) { result.Add(candidate); continue; }
            if (previousReleases.Any(previous => previous.Identity == ReleaseIdentity.For(candidate.Release.InfoHash, candidate.IndexerName, candidate.Release.Title)
                || string.Equals(previous.Title, candidate.Release.Title, StringComparison.OrdinalIgnoreCase))) {
                result.Add(Reject(candidate));
                continue;
            }
            var names = candidate.Release.KnownFileNames is { Count: > 0 } known ? known
                : inventory?.ReadFileNames(candidate.Release.DownloadUrl) ?? [];
            // A pack title cannot prove its episode contents. Only single-unit requests can use a
            // filename proposal until the downloader's normal preflight supplies actual inventory.
            if (names.Count == 0 && input.Kind == EntityKind.VideoSeason) {
                result.Add(Reject(candidate));
                continue;
            }
            if (names.Count == 0) names = [candidate.Release.Title + ".mkv"];
            var proposed = new DownloadPayload(string.Empty, names.Select(name => new ImportCandidateFile(name, 1)).ToArray());
            var coverage = await CoverageAsync(input, held, proposed, profile, cancellationToken);
            var improves = coverage.Count > 0 && coverage.IsSupersetOf(baseline)
                && (coverage.Count > baseline.Count || new MediaUpgradeSpecification(input.Kind).Evaluate(candidate.Release, rules) is null);
            result.Add(improves ? candidate : Reject(candidate));
        }
        return result;
    }

    private async Task<IReadOnlyList<SelectedRelease>?> ReadPreviousReleasesAsync(Guid heldId, bool retainCurrentIdentity,
        CancellationToken cancellationToken) {
        var releases = new List<SelectedRelease>();
        var visited = new HashSet<Guid>();
        Guid? current = heldId;
        while (current is { } id) {
            if (!visited.Add(id)) return null;
            if ((id != heldId || retainCurrentIdentity)
                && await acquisitions.GetSelectedReleaseAsync(id, cancellationToken) is { } release) releases.Add(release);
            current = (await acquisitions.GetSearchInputAsync(id, cancellationToken))?.RecoveryOfAcquisitionId;
        }
        return releases;
    }

    private async Task<HashSet<(int Season, int Episode)>> CoverageAsync(AcquisitionSearchInput input,
        AcquisitionImportContext held, DownloadPayload payload, BookImportProfile? profile, CancellationToken cancellationToken) {
        var titles = input.EpisodeCatalog.Where(season => season.SeasonNumber == input.SeasonNumber)
            .SelectMany(season => season.Episodes).ToArray();
        if (AcquisitionPayloadValidation.FindConflict(payload.Files.Select(file => file.RelativePath).ToArray(),
                input.Kind, input.WorkTitle, input.Year, input.SeasonNumber, input.EpisodeNumber,
                episodeTitle: input.Title, absoluteEpisodeNumber: input.AbsoluteEpisodeNumber,
                episodeTitles: titles, alternativeWorkTitles: input.AlternativeWorkTitles) is not null) return [];
        if (input.Kind == EntityKind.Movie) {
            var plan = MovieImportPlanBuilder.Plan(payload.Files, new(input.Title, input.Author, input.Year), profile?.PathTemplate);
            return plan.Blocked || plan.Items.Count == 0 ? [] : [(0, 0)];
        }
        if (input.SeasonNumber is not { } number) return [];
        var planned = await new TvAcquisitionImportPlanner(targets).PlanAsync(held with {
            Series = input.WorkTitle, SeasonNumber = number, EpisodeNumber = input.EpisodeNumber,
            ProfileId = input.ProfileId, AlternativeWorkTitles = input.AlternativeWorkTitles
        }, payload, profile, null, cancellationToken);
        if (planned.Plan.Blocked) return [];
        var wanted = planned.Catalog.Where(season => season.SeasonNumber == number)
            .SelectMany(season => season.Episodes).Where(episode => episode.IsWanted)
            .Select(episode => (number, episode.Episode)).ToHashSet();
        var coverage = planned.Plan.Units.SelectMany(unit => unit.ExtraEpisodes.Prepend(unit.Episode)
            .Select(episode => (unit.Season, episode))).Where(wanted.Contains).ToHashSet();
        return input.EpisodeNumber is { } requested && !coverage.Contains((number, requested)) ? [] : coverage;
    }

    private static ScoredRelease Reject(ScoredRelease candidate) => candidate with {
        Accepted = false, Rejections = candidate.Rejections.Append(ReleaseRejectionReason.NotAnUpgrade).Distinct().ToArray()
    };
}
