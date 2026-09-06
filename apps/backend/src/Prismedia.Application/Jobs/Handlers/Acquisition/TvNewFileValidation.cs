using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Inspects every pending new episode before a checkpoint places any files, including on retry.</summary>
internal sealed class TvNewFileValidation(IMediaProbe probe, IBookAcquisitionProfileStore profiles,
    IImportTargetIndex targets, IMonitorStore? monitors) {
    /// <summary>Returns a review reason for unreadable or disallowed new files; installed units and replacements keep their own recovery rules.</summary>
    public async Task<string?> ValidateAsync(AcquisitionImportContext import, DownloadPayload? payload,
        TvImportCheckpoint checkpoint, SelectedRelease? selected, CancellationToken token) {
        var pending = checkpoint.Units.Where(unit => unit.PreviousFilePath is null && !unit.AdoptedExistingTarget
            && (unit.FinalPath is null || !File.Exists(unit.FinalPath)))
            .Select(unit => (Unit: unit, Path: SourcePath(unit, payload)))
            .Where(item => item.Path is not null && File.Exists(item.Path)).ToArray();
        if (pending.Length == 0) return null;

        var manual = selected is { ManualPick: true };
        var rulesBySeason = new Dictionary<int, BookAcquisitionRules>();
        if (!manual) {
            var requestedRules = await profiles.GetRulesAsync(import.ProfileId, import.Kind, token);
            var foreignNumbers = pending.Select(item => item.Unit.SeasonNumber).Distinct()
                .Where(number => import.SeasonNumber is { } requested && number != requested).ToArray();
            var catalog = foreignNumbers.Length > 0 && import.EntityId is { } entityId
                ? await targets.GetSeriesEpisodeCatalogAsync(entityId, token) : [];
            var foreignIds = catalog.Where(season => foreignNumbers.Contains(season.SeasonNumber))
                .Select(season => season.SeasonEntityId).OfType<Guid>().Distinct().ToArray();
            var destinationMonitors = monitors is not null && foreignIds.Length > 0
                ? await monitors.ListByEntityIdsAsync(foreignIds, token) : null;
            foreach (var number in pending.Select(item => item.Unit.SeasonNumber).Distinct()) {
                var rules = requestedRules;
                if (foreignNumbers.Contains(number)) {
                    var destinations = catalog.Where(season => season.SeasonNumber == number).ToArray();
                    if (destinations.Length != 1 || destinations[0].SeasonEntityId is not { } destinationId
                        || destinationMonitors is null || !destinationMonitors.TryGetValue(destinationId, out var monitor)) {
                        return "The extra episode's destination profile could not be resolved. The download was preserved for review.";
                    }
                    // Election already captured monitored extras. A later pause does not invalidate that
                    // identity, but the destination's current quality and language settings still apply.
                    rules = await profiles.GetRulesAsync(monitor.ProfileId, EntityKind.VideoSeason, token);
                }
                rulesBySeason[number] = rules;
            }
        }

        foreach (var (unit, path) in pending) {
            var video = await probe.ProbeVideoAsync(path!, token);
            if (video is not { Width: > 0, Height: > 0, DurationSeconds: > 0 }
                || !double.IsFinite(video.DurationSeconds.Value)) {
                return $"The episode file '{Path.GetFileName(unit.SourceRelativePath)}' could not be verified as readable video with a valid runtime. The download was preserved for review.";
            }
            if (!manual && VideoPayloadProfileValidation.Validate(video,
                    VideoPayloadProfileValidation.ClaimedQuality(unit.SourceRelativePath, selected?.Title).ToCode(),
                    rulesBySeason[unit.SeasonNumber]) is { } reason) {
                return $"{Path.GetFileName(unit.SourceRelativePath)}: {reason}";
            }
        }
        return null;
    }

    private static string? SourcePath(TvImportCheckpointUnit unit, DownloadPayload? payload) =>
        !string.IsNullOrWhiteSpace(unit.SourceAbsolutePath) ? Path.GetFullPath(unit.SourceAbsolutePath)
            : payload is null ? null : Path.GetFullPath(Path.Combine(payload.ContentRoot, unit.SourceRelativePath));
}
