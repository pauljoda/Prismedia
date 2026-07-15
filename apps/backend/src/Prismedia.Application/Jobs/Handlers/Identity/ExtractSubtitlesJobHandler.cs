using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Application.Subtitles;

namespace Prismedia.Application.Jobs.Handlers.Identity;

/// <summary>
/// Reconciles embedded text streams and adjacent subtitle sidecars into app-owned subtitle assets.
/// </summary>
public sealed class ExtractSubtitlesJobHandler(
    ILogger<ExtractSubtitlesJobHandler> logger,
    IMediaProbe mediaProbe,
    ISubtitleAssetService assets,
    IMediaProcessingStatePersistence persistence,
    ISubtitleSidecarDiscovery sidecars,
    IAutomaticSubtitleAcquisitionScheduler? acquisitionScheduler = null) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.ExtractSubtitles;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(5, "Discovering subtitle files", cancellationToken);

        var discovery = (await sidecars.DiscoverAsync([filePath], cancellationToken)).FirstOrDefault();
        if (discovery is null || !discovery.IsComplete) {
            throw new IOException("Adjacent subtitle discovery could not read the video's directory reliably.");
        }

        await context.ReportProgressAsync(15, "Probing embedded subtitle streams", cancellationToken);

        var streams = await mediaProbe.ProbeSubtitleStreamsAsync(filePath, cancellationToken);
        var tracks = new List<ManagedSubtitleTrackData>(streams.Count + discovery.Candidates.Count);
        if (streams.Count > 0) {
            await context.ReportProgressAsync(30, $"Extracting {streams.Count} embedded subtitle streams", cancellationToken);
            var extractedPaths = await assets.ExtractSubtitlesAsync(
                entityId, filePath, streams, cancellationToken);
            if (extractedPaths.Count != streams.Count) {
                throw new IOException(
                    $"Only {extractedPaths.Count} of {streams.Count} embedded subtitle streams were extracted.");
            }

            foreach (var (stream, path) in streams.Zip(extractedPaths)) {
                tracks.Add(new ManagedSubtitleTrackData(
                    EntitySubtitleSource.Embedded,
                    SubtitleSourceKeys.EmbeddedStream(stream.StreamIndex),
                    string.IsNullOrWhiteSpace(stream.Language)
                        ? SubtitleLanguages.Undetermined
                        : stream.Language,
                    stream.Title,
                    SubtitleFormats.Vtt,
                    path,
                    string.IsNullOrWhiteSpace(stream.CodecName)
                        ? SubtitleFormats.Unknown
                        : stream.CodecName,
                    stream.StreamIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        await context.ReportProgressAsync(60, $"Importing {discovery.Candidates.Count} adjacent subtitle files", cancellationToken);
        var failedCandidateCount = 0;
        foreach (var candidate in discovery.Candidates) {
            try {
                var imported = await assets.ImportSidecarSubtitleAsync(
                    entityId,
                    candidate.Path,
                    candidate.SourceKey,
                    candidate.Format,
                    cancellationToken);
                tracks.Add(new ManagedSubtitleTrackData(
                    EntitySubtitleSource.Sidecar,
                    candidate.SourceKey,
                    candidate.Language,
                    candidate.Label,
                    SubtitleFormats.Vtt,
                    imported.StoragePath,
                    candidate.Format,
                    imported.SourcePath));
            } catch (SubtitleAssetImportException exception) {
                failedCandidateCount++;
                logger.LogWarning(
                    exception,
                    "ExtractSubtitles: skipped unreadable sidecar {SidecarPath} for {Label}",
                    candidate.Path,
                    context.Job.TargetLabel);
            }
        }

        await context.ReportProgressAsync(90, "Reconciling subtitle tracks", cancellationToken);
        // Content-addressed assets cannot be rolled back safely after a persistence exception: a
        // timeout or cancellation may be an ambiguous commit. Definite non-commits are collected by
        // orphan maintenance after its grace period instead of risking a committed row's asset.
        var reconciliation = await Persistence.ReconcileManagedSubtitlesAsync(
            entityId,
            discovery.Signature,
            tracks,
            isComplete: failedCandidateCount == 0,
            cancellationToken);

        // The database now references the new generation. Cleanup is strictly post-commit and
        // best effort: it must never roll back or delete assets that committed rows now serve.
        try {
            await assets.DeleteSubtitleAssetsAsync(
                reconciliation.ObsoleteAssetPaths, CancellationToken.None);
        } catch (Exception exception) {
            logger.LogWarning(exception, "ExtractSubtitles: obsolete subtitle assets could not be cleaned up");
        }

        logger.LogInformation(
            "ExtractSubtitles: reconciled {EmbeddedCount} embedded and {SidecarCount} adjacent tracks for {Label}; {FailedCount} sidecars need retry",
            streams.Count,
            tracks.Count(track => track.Source == EntitySubtitleSource.Sidecar),
            context.Job.TargetLabel,
            failedCandidateCount);

        if (failedCandidateCount == 0 && acquisitionScheduler is not null) {
            await acquisitionScheduler.ScheduleAsync(
                entityId,
                context.Job.TargetLabel ?? entityId.ToString(),
                cancellationToken);
        }

        var completionMessage = failedCandidateCount == 0
            ? $"Reconciled {tracks.Count} subtitles"
            : $"Reconciled {tracks.Count} subtitles; {failedCandidateCount} sidecars need retry";
        await context.ReportProgressAsync(100, completionMessage, cancellationToken);
    }
}
