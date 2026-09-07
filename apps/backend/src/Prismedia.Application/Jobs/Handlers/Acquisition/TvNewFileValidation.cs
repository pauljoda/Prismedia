using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Verifies recovered files and inspects pending new episodes before a checkpoint advances.</summary>
internal sealed class TvNewFileValidation(IMediaProbe probe, IBookAcquisitionProfileStore profiles,
    IImportTargetIndex targets, IMonitorStore? monitors, IVideoPayloadVerifier verifier) {
    private readonly Dictionary<string, VideoProbeData?> _observedVideo = new(FileSystemPathComparison.Comparer);
    private readonly Dictionary<string, string> _decodeFailures = new(FileSystemPathComparison.Comparer);
    public IReadOnlyDictionary<string, string> DecodeFailures => _decodeFailures;
    public bool DecodedAllPendingFiles { get; private set; }
    /// <summary>Returns a review reason for damaged recovered files or unreadable/disallowed pending files.</summary>
    public Task<string?> ValidateAsync(JobContext context, AcquisitionImportContext import, DownloadPayload? payload,
        TvImportCheckpoint checkpoint, SelectedRelease? selected, CancellationToken token) =>
        ValidateCoreAsync(context, import, payload, checkpoint, selected, true, token);

    /// <summary>Rechecks current profile and measured metadata after a long decode, without decoding the same bytes twice.</summary>
    public Task<string?> ValidateCurrentProfileAsync(JobContext context, AcquisitionImportContext import, DownloadPayload? payload,
        TvImportCheckpoint checkpoint, SelectedRelease? selected, CancellationToken token) =>
        ValidateCoreAsync(context, import, payload, checkpoint, selected, false, token);

    private async Task<string?> ValidateCoreAsync(JobContext context, AcquisitionImportContext import, DownloadPayload? payload,
        TvImportCheckpoint checkpoint, SelectedRelease? selected, bool decode, CancellationToken token) {
        // A crash may happen either side of recording the move. In both cases the library-side
        // bytes still need integrity verification before recovery publishes their catalog bindings.
        // Already placed units retain their elected profile decision; only pending files recheck it.
        var recovered = checkpoint.Units.Where(unit => !unit.AdoptedExistingTarget)
            .Select(unit => !string.IsNullOrWhiteSpace(unit.FinalPath) && File.Exists(unit.FinalPath)
                ? unit.FinalPath
                : unit.PreviousFilePath is null && !File.Exists(SourcePath(unit, payload)) && File.Exists(unit.TargetAbsolutePath)
                    ? unit.TargetAbsolutePath : null).OfType<string>();
        if (decode && await TvPlacedFileValidation.ValidateAsync(context, recovered, verifier, token) is { } recoveryHold) return recoveryHold;

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

        var verified = 0;
        foreach (var (unit, path) in pending) {
            var video = decode ? await probe.ProbeVideoAsync(path!, token) : _observedVideo.GetValueOrDefault(path!);
            if (decode) _observedVideo[path!] = video;
            if (video is not { Width: > 0, Height: > 0, DurationSeconds: > 0 }
                || !double.IsFinite(video.DurationSeconds.Value)) {
                return $"The episode file '{Path.GetFileName(unit.SourceRelativePath)}' could not be verified as readable video with a valid runtime. The download was preserved for review.";
            }
            if (!manual && VideoPayloadProfileValidation.Validate(video,
                    VideoPayloadProfileValidation.ClaimedQuality(unit.SourceRelativePath, selected?.Title).ToCode(),
                    rulesBySeason[unit.SeasonNumber]) is { } reason) {
                return $"{Path.GetFileName(unit.SourceRelativePath)}: {reason}";
            }
            if (!decode) continue;
            await context.ReportProgressAsync(30, $"Verifying episode {++verified} of {pending.Length}", token);
            if (await verifier.FindFailureAsync(path!, token) is { } failure) {
                _decodeFailures[unit.SourceRelativePath.Replace('\\', '/')] = failure;
            }
        }
        if (decode) {
            DecodedAllPendingFiles = true;
            if (_decodeFailures.FirstOrDefault() is { Key: not null } failed) {
                return $"{Path.GetFileName(failed.Key)}: {failed.Value}";
            }
        }
        return null;
    }

    private static string? SourcePath(TvImportCheckpointUnit unit, DownloadPayload? payload) =>
        !string.IsNullOrWhiteSpace(unit.SourceAbsolutePath) ? Path.GetFullPath(unit.SourceAbsolutePath)
            : payload is null ? null : Path.GetFullPath(Path.Combine(payload.ContentRoot, unit.SourceRelativePath));
}
