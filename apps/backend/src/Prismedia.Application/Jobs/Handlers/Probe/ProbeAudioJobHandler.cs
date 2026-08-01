using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Probe;

/// <summary>
/// Probes an audio file via ffprobe to extract duration, codec, bitrate, sample rate, channels,
/// and embedded tags (artist, album, title), then chains waveform generation if enabled.
/// </summary>
public sealed class ProbeAudioJobHandler(
    ILogger<ProbeAudioJobHandler> logger,
    IMediaProbe mediaProbe,
    IMediaProcessingStatePersistence persistence,
    ILibraryScanRootPersistence roots,
    IDownstreamNeedsPersistence downstreamNeeds) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.ProbeAudio;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(10, "Probing audio metadata", cancellationToken);

        var probe = await mediaProbe.ProbeAudioAsync(filePath, cancellationToken);
        if (probe is null) {
            // Persist the marker BEFORE failing: it stops scans from re-enqueueing probe and
            // waveform work for this entity until the file changes on disk, so one corrupt
            // file surfaces here once instead of churning the queue forever.
            await Persistence.MarkEntityProbeFailedAsync(entityId, cancellationToken);
            throw new InvalidOperationException(
                $"ffprobe could not read '{filePath}' — the file appears corrupt or truncated. " +
                "Probing and waveform generation are paused for it until the file is repaired or replaced.");
        }

        await Persistence.UpsertEntityTechnicalAsync(entityId,
            probe.DurationSeconds, null, null, null, probe.BitRate,
            probe.SampleRate, probe.Channels, probe.Codec, probe.Container, null,
            cancellationToken);
        await Persistence.UpsertMediaSourceAsync(
            entityId,
            filePath,
            new MediaSourceProbeData(
                probe.DurationSeconds,
                probe.FileSize,
                probe.BitRate,
                probe.Container,
                null,
                probe.Codec,
                null,
                null,
                null),
            [new MediaStreamProbeData(
                0,
                "Audio",
                probe.Codec,
                null,
                "Audio",
                null,
                null,
                null,
                probe.BitRate,
                probe.SampleRate,
                probe.Channels,
                IsDefault: true,
                IsForced: false)],
            cancellationToken);

        var trackNumber = ParseTrackNumber(probe.TrackNumber);
        if (probe.Artist is not null || probe.Album is not null || trackNumber is not null) {
            await Persistence.UpsertAudioTrackTagsAsync(entityId, probe.Artist, probe.Album, trackNumber, cancellationToken);
        }

        var settings = await roots.GetSettingsAsync(cancellationToken);
        var needs = await downstreamNeeds.CheckDownstreamNeedsBatchAsync([entityId], cancellationToken);
        var processing = EntityKindRegistry.Describe(EntityKind.AudioTrack).Processing;
        var plan = needs.TryGetValue(entityId, out var entityNeeds)
            ? processing.Plan(EntityProcessingInputAdapter.From(
                settings,
                entityNeeds,
                forceSubtitleReconciliationForOwnedSource: false))
            : null;
        if (plan?.PreviewJobType is { } previewJobType) {
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    previewJobType,
                    EntityKind.AudioTrack,
                    entityId.ToString(),
                    context.Job.TargetLabel),
                cancellationToken);
        }

        logger.LogInformation("ProbeAudio: {Label} — {Duration:F1}s {Codec} {SampleRate}Hz",
            context.Job.TargetLabel, probe.DurationSeconds, probe.Codec, probe.SampleRate);

        await context.ReportProgressAsync(100, "Probe complete", cancellationToken);
    }

    /// <summary>
    /// Parses a track number from an embedded <c>track</c> tag, which can be "7", "07", or "7/14".
    /// Returns null when there is no usable leading number.
    /// </summary>
    private static int? ParseTrackNumber(string? tag) {
        if (string.IsNullOrWhiteSpace(tag)) {
            return null;
        }

        var head = tag.Split('/')[0].Trim();
        return int.TryParse(head, out var number) && number > 0 ? number : null;
    }
}
