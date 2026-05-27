using Prismedia.Application.Jobs.Handlers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates audio waveform peak data by decoding to PCM via ffmpeg and computing
/// min/max sample pairs at 20 pixels per second.
/// </summary>
public sealed class GenerateAudioWaveformJobHandler(
    ILogger<GenerateAudioWaveformJobHandler> logger,
    IMediaAssetGenerator assets,
    IMediaProcessingStatePersistence persistence) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.GenerateAudioWaveform;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(10, "Generating waveform data", cancellationToken);

        var probe = await GetDurationAsync(entityId, cancellationToken);
        if (probe is null or <= 0) {
            logger.LogWarning("GenerateAudioWaveform: no duration for {EntityId}, skipping", entityId);
            return;
        }

        var waveformData = await assets.GenerateWaveformDataAsync(filePath, probe.Value, 20, cancellationToken);
        if (waveformData is null) {
            logger.LogWarning("GenerateAudioWaveform: PCM decode failed for {Label}", context.Job.TargetLabel);
            return;
        }

        var outputPath = assets.AudioWaveformPath(entityId);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = JsonSerializer.Serialize(new { data = waveformData });
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        var size = new FileInfo(outputPath).Length;
        await Persistence.UpsertEntityFileAsync(entityId, EntityFileRole.Waveform, assets.AudioWaveformUrl(entityId), "application/json", size, cancellationToken);

        logger.LogInformation("GenerateAudioWaveform: created waveform for {Label} ({Pixels} pixels)",
            context.Job.TargetLabel, waveformData.Length / 2);

        await context.ReportProgressAsync(100, "Waveform complete", cancellationToken);
    }

    private async Task<double?> GetDurationAsync(Guid entityId, CancellationToken cancellationToken) {
        var tech = await Persistence.GetEntityTechnicalAsync(entityId, cancellationToken);
        return tech?.DurationSeconds;
    }
}
