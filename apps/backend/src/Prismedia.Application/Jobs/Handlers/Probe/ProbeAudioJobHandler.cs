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
            logger.LogWarning("ProbeAudio: ffprobe failed for {Path}", filePath);
            return;
        }

        await Persistence.UpsertEntityTechnicalAsync(entityId,
            probe.DurationSeconds, null, null, null, probe.BitRate,
            probe.SampleRate, probe.Channels, probe.Codec, probe.Container, null,
            cancellationToken);

        var trackNumber = ParseTrackNumber(probe.TrackNumber);
        if (probe.Artist is not null || probe.Album is not null || trackNumber is not null) {
            await Persistence.UpsertAudioTrackTagsAsync(entityId, probe.Artist, probe.Album, trackNumber, cancellationToken);
        }

        var settings = await roots.GetSettingsAsync(cancellationToken);
        if (settings.AutoGeneratePreview && !await downstreamNeeds.HasEntityFileAsync(entityId, EntityFileRole.Waveform, cancellationToken)) {
            await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                JobType.GenerateAudioWaveform, TargetEntityKind: "audio-track",
                TargetEntityId: entityId.ToString(), TargetLabel: context.Job.TargetLabel), cancellationToken);
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
