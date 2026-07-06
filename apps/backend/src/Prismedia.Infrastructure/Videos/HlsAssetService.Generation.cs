using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Media;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Virtual HLS rendition generation: ffmpeg session lifecycle, segment waiting, and canonical-cache promotion for <see cref="HlsAssetService"/>.
/// </summary>
public sealed partial class HlsAssetService {
    private VirtualRenditionGeneration StartVirtualRenditionGeneration(
        Guid id,
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int? audioStreamIndex,
        int startSegment) {
        var endSegment = SegmentCount(source.DurationSeconds!.Value) - 1;
        var key = $"{id}/{audioCacheKey}/{rendition.Name}/{startSegment}";
        if (ActiveRenditions.TryGetValue(key, out var activeGeneration)) {
            return activeGeneration;
        }

        var stagingDirectory = VirtualPath(id, audioCacheKey, "v", rendition.Name, $".gen_{startSegment:00000}_{Guid.NewGuid():N}");
        var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generation = new VirtualRenditionGeneration(
            startSegment,
            endSegment,
            stagingDirectory,
            cancellation,
            completion.Task,
            EntityId: id,
            StartedAtUtc: DateTimeOffset.UtcNow);

        if (!ActiveRenditions.TryAdd(key, generation)) {
            cancellation.Dispose();
            if (ActiveRenditions.TryGetValue(key, out activeGeneration)) {
                return activeGeneration;
            }

            return StartVirtualRenditionGeneration(
                id,
                source,
                rendition,
                audioCacheKey,
                audioStreamIndex,
                startSegment);
        }

        _ = CompleteVirtualRenditionGenerationAsync(
            completion,
            id,
            source,
            rendition,
            audioCacheKey,
            audioStreamIndex,
            startSegment,
            stagingDirectory,
            key,
            cancellation.Token);

        return generation;
    }

    private async Task CompleteVirtualRenditionGenerationAsync(
        TaskCompletionSource completion,
        Guid id,
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int? audioStreamIndex,
        int startSegment,
        string stagingDirectory,
        string generationKey,
        CancellationToken cancellationToken) {
        try {
            await GenerateVirtualRenditionAsync(
                id,
                source,
                rendition,
                audioCacheKey,
                audioStreamIndex,
                startSegment,
                stagingDirectory,
                generationKey,
                cancellationToken);
            completion.TrySetResult();
        } catch (OperationCanceledException) {
            completion.TrySetCanceled(cancellationToken);
        } catch (Exception ex) {
            completion.TrySetException(ex);
        }
    }

    private async Task GenerateVirtualRenditionAsync(
        Guid id,
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int? audioStreamIndex,
        int startSegment,
        string stagingDirectory,
        string generationKey,
        CancellationToken cancellationToken) {
        if (_processes is null) {
            throw new InvalidOperationException("HLS rendition generation requires a process executor.");
        }

        var playlistPath = Path.Combine(stagingDirectory, "index.generated.m3u8");
        var segmentPattern = Path.Combine(stagingDirectory, "seg_%05d.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(playlistPath)!);
        var transcoderOptions = await ResolveTranscoderOptionsAsync(cancellationToken);
        var transcoderProfile = ResolveTranscoderProfile(transcoderOptions);
        var effectiveTranscoderProfile = ResolveEffectiveTranscoderProfile(source, transcoderProfile);
        var threadCount = ResolveEncoderThreadCount(transcoderOptions.EncodingThreadCount);
        if (effectiveTranscoderProfile != transcoderProfile) {
            _logger?.LogInformation(
                "Virtual HLS generation for HDR/Dolby Vision source {VideoId} is using software tone mapping instead of {TranscoderProfile}.",
                id,
                transcoderProfile);
        }

        ProcessExecutionResult result;
        try {
            result = await _processes.RunAsync(
                transcoderOptions.FfmpegPath,
                VirtualRenditionArguments(
                    source,
                    rendition,
                    audioStreamIndex,
                    startSegment,
                    playlistPath,
                    segmentPattern,
                    effectiveTranscoderProfile,
                    transcoderOptions.VaapiDevice,
                    threadCount),
                environment: null,
                cancellationToken);

            if (result.ExitCode != 0 && effectiveTranscoderProfile != HlsTranscoderProfile.Software) {
                _logger?.LogWarning(
                    "Virtual HLS generation using {TranscoderProfile} failed for {VideoId} rendition {Rendition}; retrying with software x264. Error: {Error}",
                    effectiveTranscoderProfile,
                    id,
                    rendition.Name,
                    result.StandardError);

                ResetStagingDirectory(stagingDirectory);
                result = await _processes.RunAsync(
                    transcoderOptions.FfmpegPath,
                    VirtualRenditionArguments(
                        source,
                        rendition,
                        audioStreamIndex,
                        startSegment,
                        playlistPath,
                        segmentPattern,
                        HlsTranscoderProfile.Software,
                        transcoderOptions.VaapiDevice,
                        threadCount),
                    environment: null,
                    cancellationToken);
            }

            if (result.ExitCode != 0 &&
                NeedsToneMapping(source) &&
                IsMissingVideoFilterError(result.StandardError)) {
                _logger?.LogWarning(
                    "Virtual HLS tone mapping failed for {VideoId} rendition {Rendition}; retrying with basic software SDR output. Error: {Error}",
                    id,
                    rendition.Name,
                    result.StandardError);

                ResetStagingDirectory(stagingDirectory);
                result = await _processes.RunAsync(
                    transcoderOptions.FfmpegPath,
                    VirtualRenditionArguments(
                        source,
                        rendition,
                        audioStreamIndex,
                        startSegment,
                        playlistPath,
                        segmentPattern,
                        HlsTranscoderProfile.Software,
                        transcoderOptions.VaapiDevice,
                        threadCount,
                        enableToneMapping: false),
                    environment: null,
                    cancellationToken);
            }
        } finally {
            ActiveRenditions.TryRemove(generationKey, out var _);
        }

        if (result.ExitCode != 0) {
            _logger?.LogWarning(
                "Virtual HLS rendition generation failed for {VideoId} rendition {Rendition}: {Error}",
                id,
                rendition.Name,
                result.StandardError);
            throw new InvalidOperationException("HLS rendition generation failed.");
        }

        foreach (var segmentPath in Directory.EnumerateFiles(stagingDirectory, "seg_*.ts")) {
            CopySegmentToCanonical(id, rendition, audioCacheKey, segmentPath);
        }
    }

    private async Task WaitForVirtualSegmentAsync(
        Guid id,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int segmentIndex,
        string outputPath,
        VirtualRenditionGeneration generation,
        CancellationToken cancellationToken) {
        var stagedPath = Path.Combine(generation.StagingDirectory, $"seg_{segmentIndex:00000}.ts");
        while (true) {
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) {
                return;
            }

            if (File.Exists(stagedPath) && new FileInfo(stagedPath).Length > 0) {
                CopySegmentToCanonical(id, rendition, audioCacheKey, stagedPath);
                return;
            }

            if (generation.Task.IsCompleted) {
                try {
                    await generation.Task;
                } catch (OperationCanceledException ex) {
                    throw new FileNotFoundException(
                        $"HLS rendition generation was replaced before segment {segmentIndex} was produced for {id}/{rendition.Name}.",
                        ex);
                }

                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) {
                    return;
                }

                if (File.Exists(stagedPath) && new FileInfo(stagedPath).Length > 0) {
                    CopySegmentToCanonical(id, rendition, audioCacheKey, stagedPath);
                    return;
                }

                throw new FileNotFoundException(
                    $"HLS rendition generation completed without segment {segmentIndex} for {id}/{rendition.Name}.");
            }

            await Task.Delay(SegmentPollInterval, cancellationToken);
        }
    }

    private void CopySegmentToCanonical(
        Guid id,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        string stagedPath) {
        if (!File.Exists(stagedPath) || new FileInfo(stagedPath).Length == 0) {
            return;
        }

        var outputPath = VirtualPath(id, audioCacheKey, "v", rendition.Name, Path.GetFileName(stagedPath));
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tempPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        File.Copy(stagedPath, tempPath, overwrite: true);
        File.Move(tempPath, outputPath, overwrite: true);
    }
}
