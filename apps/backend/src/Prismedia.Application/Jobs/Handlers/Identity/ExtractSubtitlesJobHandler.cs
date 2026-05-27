using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Identity;

/// <summary>
/// Probes a video file for embedded text subtitle streams, extracts them to WebVTT files,
/// and records them in the entity_subtitles table.
/// </summary>
public sealed class ExtractSubtitlesJobHandler(
    ILogger<ExtractSubtitlesJobHandler> logger,
    IMediaProbe mediaProbe,
    IMediaAssetGenerator assets,
    IMediaProcessingStatePersistence persistence) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.ExtractSubtitles;

    protected override Task OnSourceFileNotFoundAsync(Guid entityId, CancellationToken cancellationToken) =>
        Persistence.MarkSubtitlesExtractedAsync(entityId, cancellationToken);

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(10, "Probing subtitle streams", cancellationToken);

        var streams = await mediaProbe.ProbeSubtitleStreamsAsync(filePath, cancellationToken);
        if (streams.Count == 0) {
            logger.LogInformation("ExtractSubtitles: no text subtitles in {Label}", context.Job.TargetLabel);
            await Persistence.MarkSubtitlesExtractedAsync(entityId, cancellationToken);
            await context.ReportProgressAsync(100, "No subtitles found", cancellationToken);
            return;
        }

        await context.ReportProgressAsync(30, $"Extracting {streams.Count} subtitle streams", cancellationToken);

        var outputDir = assets.SubtitleDir(entityId);
        var extractedPaths = await assets.ExtractSubtitlesAsync(filePath, outputDir, streams, cancellationToken);

        await context.ReportProgressAsync(80, "Recording subtitle tracks", cancellationToken);

        foreach (var path in extractedPaths) {
            var fileName = Path.GetFileNameWithoutExtension(path);
            var parts = fileName.Split('-');
            var language = parts.Length >= 2 ? parts[1] : "und";
            var indexStr = parts.Length >= 3 ? parts[2] : "0";
            int.TryParse(indexStr, out var streamIndex);

            var matchingStream = streams.FirstOrDefault(s => s.StreamIndex.ToString() == indexStr);
            var label = matchingStream?.Title;

            await Persistence.UpsertSubtitleAsync(entityId, language, label, "vtt",
                EntitySubtitleSource.Embedded, path, matchingStream?.CodecName ?? "unknown", streamIndex, cancellationToken);
        }

        await Persistence.MarkSubtitlesExtractedAsync(entityId, cancellationToken);

        logger.LogInformation("ExtractSubtitles: extracted {Count} subtitle tracks from {Label}",
            extractedPaths.Count, context.Job.TargetLabel);

        await context.ReportProgressAsync(100, $"Extracted {extractedPaths.Count} subtitles", cancellationToken);
    }
}
