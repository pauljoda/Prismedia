using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Combines physical episode coverage with measured per-file upgrade evidence before any pack placement begins.</summary>
internal sealed class TvMeasuredMergePlanner(IMediaUpgradePayloadInspector inspector) {
    /// <summary>Probes colliding files once each. Exact byte matches need no replacement; uncertain upgrades preserve the complete payload for review.</summary>
    public async Task<TvMeasuredMergePlan> PlanAsync(
        IReadOnlyList<TvPlanUnit> units,
        TvSeriesDiskLayout layout,
        Func<int, string> seasonSegment,
        DownloadPayload payload,
        SelectedRelease? selected,
        BookAcquisitionRules rules,
        bool allowFormatChange,
        CancellationToken cancellationToken) {
        var decisions = new Dictionary<string, MergeFileAction>(FileSystemPathComparison.Comparer);
        var claims = units.ToDictionary(unit => unit.SourceRelativePath,
            unit => Claim(unit, selected), FileSystemPathComparison.Comparer);
        var initial = Plan((unit, owned) => {
            var (quality, revision) = claims[unit.SourceRelativePath];
            return TvExistingTargetMerge.DecideAgainstOwned(unit.FileName, owned, (int)quality,
                revision, rules.ProperPolicy, allowFormatChange);
        });
        var matchingExisting = TvImportExecutionSupport.MatchingExistingFiles(
            initial.Select(item => item.OwnedFilePath is null ? item : item with { Action = MergeFileAction.DropNotUpgrade }).ToArray(), payload);
        var unitsBySource = units.ToDictionary(unit => unit.SourceRelativePath, FileSystemPathComparison.Comparer);
        foreach (var item in initial) {
            if (item.OwnedFilePath is not { } owned || matchingExisting.Contains(item.SourceRelativePath)) {
                continue;
            }
            var inspection = await inspector.InspectAsync(owned,
                Path.GetFullPath(Path.Combine(payload.ContentRoot, item.SourceRelativePath)), cancellationToken);
            var (quality, revision) = claims[item.SourceRelativePath];
            var ownedQuality = VideoQualityDetection.Detect(Path.GetFileNameWithoutExtension(owned));
            var knownLowerQuality = item.Action == MergeFileAction.DropNotUpgrade && inspection is { } facts
                && MediaQualityLadder.VideoResolutionTierOf(ownedQuality.ToCode()) == facts.OwnedResolutionTier
                && MediaQualityLadder.VideoResolutionTierOf(quality.ToCode()) == facts.CandidateResolutionTier;
            if (HoldReason(inspection, quality, rules, knownLowerQuality) is { } reason) {
                return new(initial, matchingExisting, reason);
            }
            var measured = inspection!;
            var higherResolution = measured.CandidateResolutionTier > measured.OwnedResolutionTier;
            // A stale filename must not make an already-1080p copy look like a 720p source upgrade.
            var comparableSourceClaims = MediaQualityLadder.VideoResolutionTierOf(ownedQuality.ToCode()) == measured.OwnedResolutionTier;
            var unit = unitsBySource[item.SourceRelativePath];
            var action = comparableSourceClaims
                ? TvExistingTargetMerge.DecideAgainstOwned(unit.FileName, owned, (int)quality,
                    revision, rules.ProperPolicy, allowFormatChange)
                : MergeFileAction.DropNotUpgrade;
            if (higherResolution) {
                action = allowFormatChange || string.Equals(Path.GetExtension(unit.FileName), Path.GetExtension(owned), StringComparison.OrdinalIgnoreCase)
                    ? MergeFileAction.ReplaceUpgrade : MergeFileAction.DropFormatChange;
            }
            decisions[item.SourceRelativePath] = action;
        }
        return new(Plan((unit, _) => decisions.GetValueOrDefault(unit.SourceRelativePath, MergeFileAction.DropNotUpgrade)),
            matchingExisting, null);

        IReadOnlyList<MergedImportItem> Plan(Func<TvPlanUnit, string, MergeFileAction> evaluate) =>
            TvExistingTargetMerge.Plan(units, layout, seasonSegment, 0, 1, rules.ProperPolicy,
                allowFormatChange, evaluate);
    }

    private static (VideoQuality Quality, int Revision) Claim(TvPlanUnit unit, SelectedRelease? selected) {
        // A heterogeneous pack's highest-quality filename says nothing about the other files.
        var fileName = Path.GetFileNameWithoutExtension(unit.SourceRelativePath);
        var quality = VideoQualityDetection.Detect(fileName);
        if (quality == VideoQuality.Unknown && selected is not null) quality = VideoQualityDetection.Detect(selected.Title);
        return (quality, Math.Max(ReleaseRevisionDetection.Detect(fileName),
            selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title)));
    }

    private static string? HoldReason(MediaUpgradePayloadInspection? inspection, VideoQuality quality, BookAcquisitionRules rules, bool knownLowerQuality) {
        if (inspection is not { OwnedResolutionTier: > 0, CandidateResolutionTier: > 0,
                OwnedDurationSeconds: > 0, CandidateDurationSeconds: > 0 }
            || !double.IsFinite(inspection.OwnedDurationSeconds.Value)
            || !double.IsFinite(inspection.CandidateDurationSeconds.Value)) {
            return "The owned and downloaded episodes could not both be inspected with a reliable runtime. Both files were preserved for review.";
        }
        if (inspection.CandidateDurationSeconds < inspection.OwnedDurationSeconds * VideoPayloadProfileValidation.MinimumAutomaticRuntimeRatio) {
            return "A downloaded episode is substantially shorter than the owned copy. Both files were preserved for review.";
        }
        if (inspection.CandidateResolutionTier < inspection.OwnedResolutionTier && !knownLowerQuality
            || MediaQualityLadder.VideoResolutionTierOf(quality.ToCode()) is { } claimed && inspection.CandidateResolutionTier < claimed) {
            return "A downloaded episode's measured resolution is lower than the owned copy or its claimed quality. Both files were preserved for review.";
        }
        return VideoPayloadProfileValidation.ValidateProfile(quality.ToCode(), inspection.CandidateAudioLanguages, rules);
    }
}

/// <summary>A measured pack merge, or a review hold that must be resolved before writing its checkpoint.</summary>
internal sealed record TvMeasuredMergePlan(
    IReadOnlyList<MergedImportItem> Items,
    IReadOnlySet<string> MatchingExisting,
    string? HoldReason);
