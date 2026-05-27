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

        if (probe.Artist is not null || probe.Album is not null) {
            await Persistence.UpsertAudioTrackTagsAsync(entityId, probe.Artist, probe.Album, cancellationToken);
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
}
