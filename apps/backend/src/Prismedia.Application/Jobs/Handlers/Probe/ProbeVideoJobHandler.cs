using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Probe;

/// <summary>
/// Probes a video file via ffprobe to extract duration, dimensions, codec, bitrate, and container
/// metadata, then stores the results in the entity's technical capability row.
/// </summary>
public sealed class ProbeVideoJobHandler(
    ILogger<ProbeVideoJobHandler> logger,
    IMediaProbe mediaProbe,
    ILibraryScanPersistence persistence) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.ProbeVideo;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        var timer = new JobPhaseTimer();
        await context.ReportProgressAsync(10, "Probing video metadata", cancellationToken);

        VideoProbeData? probe;
        using (timer.Phase("ffprobe")) {
            probe = await mediaProbe.ProbeVideoAsync(filePath, cancellationToken);
        }

        if (probe is null) {
            logger.LogWarning("ProbeVideo: ffprobe failed for {Path}", filePath);
            return;
        }

        using (timer.Phase("persist")) {
            await Persistence.UpsertEntityTechnicalAsync(entityId,
                probe.DurationSeconds, probe.Width, probe.Height, probe.FrameRate, probe.BitRate,
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
                    probe.Codec,
                    probe.AudioCodec,
                    probe.Width,
                    probe.Height,
                    probe.FrameRate),
                BuildStreams(probe),
                cancellationToken);
        }

        var report = timer.Finish();
        logger.LogInformation(
            "[METRICS] probe-video {Label} — {Duration:F1}s {Width}x{Height} {Codec} — {Timing}",
            context.Job.TargetLabel, probe.DurationSeconds, probe.Width, probe.Height, probe.Codec,
            report.ToLogString());

        await context.ReportProgressAsync(100, "Probe complete", cancellationToken);
    }

    private static IReadOnlyList<MediaStreamProbeData> BuildStreams(VideoProbeData probe) {
        if (probe.Streams is { Count: > 0 }) {
            return probe.Streams;
        }

        var streams = new List<MediaStreamProbeData>();
        if (probe.Codec is not null || probe.Width is not null || probe.Height is not null) {
            streams.Add(new MediaStreamProbeData(
                0,
                "Video",
                probe.Codec,
                null,
                "Video",
                probe.Width,
                probe.Height,
                probe.FrameRate,
                probe.BitRate,
                null,
                null,
                IsDefault: true,
                IsForced: false));
        }

        if (probe.AudioCodec is not null || probe.SampleRate is not null || probe.Channels is not null) {
            streams.Add(new MediaStreamProbeData(
                1,
                "Audio",
                probe.AudioCodec,
                null,
                "Audio",
                null,
                null,
                null,
                null,
                probe.SampleRate,
                probe.Channels,
                IsDefault: true,
                IsForced: false));
        }

        return streams;
    }
}
