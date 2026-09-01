using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates optional video scrub-preview tiles after playback-critical probes and ordinary preview
/// assets have had an opportunity to finish. Trickplay intentionally remains best-effort background work.
/// </summary>
[JobDefinition(JobType.GenerateTrickplay, ResourceClass = JobResourceClass.HeavyCpu, Importance = JobNodeImportance.BestEffort)]
public sealed class GenerateTrickplayJobHandler(
    ILogger<GenerateTrickplayJobHandler> logger,
    IMediaAssetGenerator assets,
    IMediaProcessingStatePersistence persistence,
    ILibraryScanRootPersistence roots) : EntityFileJobHandler(logger, persistence) {
    protected override async Task ExecuteAsync(
        JobContext context,
        Guid entityId,
        string filePath,
        CancellationToken cancellationToken) {
        var settings = await roots.GetSettingsAsync(cancellationToken);
        if (!settings.GenerateTrickplay) {
            await context.ReportProgressAsync(100, "Trickplay disabled", cancellationToken);
            return;
        }

        var tech = await Persistence.GetEntityTechnicalAsync(entityId, cancellationToken);
        var (duration, width, height) = (tech?.DurationSeconds, tech?.Width, tech?.Height);
        if (duration is null or <= 0 && tech?.ProbeFailedAt is not null) {
            logger.LogWarning(
                "GenerateTrickplay: skipping {EntityId} — source file could not be probed (marked unreadable)",
                entityId);
            await context.ReportProgressAsync(
                100,
                "Skipped: source file could not be read (corrupt or truncated)",
                cancellationToken);
            return;
        }

        if (duration is null or <= 0) {
            if (!EntityKindRegistry.TryDescribe(context.Job.TargetEntityKind, out var definition)
                || definition.Processing.ProbeJobType is not { } probeJobType) {
                throw new InvalidOperationException(
                    "GenerateTrickplay requires a target kind with a technical probe job.");
            }

            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    probeJobType,
                    definition.Kind,
                    entityId.ToString(),
                    context.Job.TargetLabel),
                cancellationToken);
            throw new JobRetryLaterException(
                $"Waiting for video probe metadata before generating trickplay for {entityId}.",
                TimeSpan.FromSeconds(5));
        }

        var timer = new JobPhaseTimer();
        using (timer.Phase("trickplay")) {
            await context.ReportProgressAsync(10, "Generating trickplay tiles", cancellationToken);
            var generated = await GenerateTrickplayBatchAsync(
                entityId,
                filePath,
                settings,
                duration.Value,
                width,
                height,
                cancellationToken);
            if (!generated) {
                throw new InvalidOperationException($"Failed to generate trickplay tiles for {entityId}.");
            }
        }

        var report = timer.Finish();
        logger.LogInformation(
            "[METRICS] generate-trickplay {Label} — {Timing}",
            context.Job.TargetLabel,
            report.ToLogString());
        await context.ReportProgressAsync(100, "Trickplay complete", cancellationToken);
    }

    private async Task<bool> GenerateTrickplayBatchAsync(
        Guid entityId,
        string filePath,
        LibrarySettingsData settings,
        double duration,
        int? width,
        int? height,
        CancellationToken cancellationToken) {
        var interval = Math.Max(3, settings.TrickplayIntervalSeconds);
        var frameCount = (int)(duration / interval);
        if (frameCount < 1) {
            return true;
        }

        var (frameWidth, frameHeight) = ComputeTrickplayDimensions(
            width ?? 1920,
            height ?? 1080,
            settings.TrickplayQuality);
        var frameDir = assets.TrickplayFrameDir(entityId);
        var extractedCount = await assets.ExtractTrickplayFramesBatchAsync(
            filePath,
            frameDir,
            duration,
            interval,
            frameWidth,
            frameHeight,
            QualityToJpeg(settings.TrickplayQuality),
            cancellationToken);
        if (extractedCount == 0) {
            logger.LogWarning("Trickplay batch extraction produced zero frames for {EntityId}", entityId);
            return false;
        }

        logger.LogInformation(
            "Trickplay: extracted {Count} frames in single pass (expected {Expected})",
            extractedCount,
            frameCount);

        const int columns = 5;
        const int rows = 5;
        var tileDir = assets.TrickplayTileDir(entityId, frameWidth);
        var tileCount = await assets.ComposeTiledJpegSheetsAsync(
            frameDir,
            tileDir,
            columns,
            rows,
            frameWidth,
            frameHeight,
            QualityToJpeg(settings.TrickplayQuality),
            cancellationToken);
        if (tileCount == 0) {
            logger.LogWarning("Failed to compose trickplay tiles for {EntityId}", entityId);
            return false;
        }

        await Persistence.UpsertTrickplayInfoAsync(
            entityId,
            new TrickplayInfoData(
                frameWidth,
                frameHeight,
                columns,
                rows,
                extractedCount,
                interval,
                EstimateTrickplayBandwidth(tileDir, tileCount, extractedCount, interval)),
            cancellationToken);
        await Persistence.UpsertEntityFileAsync(
            entityId,
            EntityFileRole.Trickplay,
            assets.TrickplayPlaylistUrl(entityId, frameWidth),
            MediaContentTypes.HlsPlaylist,
            null,
            cancellationToken);
        return true;
    }

    /// <summary>
    /// Trickplay frames are small scrubber-preview thumbnails, not full-resolution images.
    /// Capped at 320x180 regardless of source resolution. Quality 1 is 320 pixels wide and
    /// quality 5 is 160 pixels wide.
    /// </summary>
    private static (int Width, int Height) ComputeTrickplayDimensions(
        int sourceWidth,
        int sourceHeight,
        int quality) {
        const int maxWidth = 320;
        const int minWidth = 160;
        var q = Math.Clamp(quality, 1, 5);
        var targetWidth = maxWidth - (q - 1) * (maxWidth - minWidth) / 4;
        targetWidth = targetWidth / 2 * 2;

        var targetHeight = sourceWidth > 0
            ? targetWidth * sourceHeight / sourceWidth
            : targetWidth * 9 / 16;
        targetHeight = targetHeight / 2 * 2;
        return (targetWidth, Math.Max(2, targetHeight));
    }

    private static int QualityToJpeg(int quality) => Math.Clamp(quality + 1, 2, 6);

    private static int EstimateTrickplayBandwidth(
        string tileDir,
        int tileCount,
        int thumbnailCount,
        int interval) {
        var totalBytes = Directory.GetFiles(tileDir, "*.jpg")
            .Take(tileCount)
            .Sum(path => new FileInfo(path).Length);
        var totalSeconds = Math.Max(interval, thumbnailCount * interval);
        return (int)Math.Ceiling(totalBytes * 8d / totalSeconds);
    }
}
