using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers audio files organized by directory, creates audio library and track entities,
/// and chains downstream probe/fingerprint jobs.
/// </summary>
public sealed class ScanAudioJobHandler(
    ILogger<ScanAudioJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanPersistence persistence) : ScanJobHandler(logger, fileDiscovery, persistence) {
    public override JobType Type => JobType.ScanAudio;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanAudio;

    protected override async Task ScanRootAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanAudio: discovering audio files in {Path}", root.Path);

        var dirGroups = await FileDiscovery.DiscoverFilesByDirectoryAsync(
            root.Path, MediaCategory.Audio, root.Recursive, cancellationToken);

        logger.LogInformation("ScanAudio: found {DirCount} directories with audio in {Label}",
            dirGroups.Count, root.Label);

        var settings = await Persistence.GetSettingsAsync(cancellationToken);
        var validLibraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedDirs = 0;

        foreach (var (dirPath, audioFiles) in dirGroups) {
            var libraryTitle = Path.GetFileName(dirPath);
            validLibraryPaths.Add(dirPath);

            var libraryId = await Persistence.UpsertAudioLibraryAsync(dirPath, libraryTitle, root.Id, root.IsNsfw, cancellationToken);
            var validTrackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < audioFiles.Count; i++) {
                var filePath = audioFiles[i];
                var title = Path.GetFileNameWithoutExtension(filePath);
                validTrackPaths.Add(filePath);

                var trackId = await Persistence.UpsertAudioTrackAsync(filePath, title, libraryId, i, root.IsNsfw, cancellationToken);

                if (settings.AutoGenerateMetadata && !await Persistence.HasEntityTechnicalAsync(trackId, cancellationToken)) {
                    await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.ProbeAudio, TargetEntityKind: "audio-track",
                        TargetEntityId: trackId.ToString(), TargetLabel: title), cancellationToken);
                }

                if (settings.AutoGenerateFingerprints && !await Persistence.HasEntityFingerprintAsync(trackId, FingerprintAlgorithm.Md5, cancellationToken)) {
                    await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.FingerprintAudio, TargetEntityKind: "audio-track",
                        TargetEntityId: trackId.ToString(), TargetLabel: title), cancellationToken);
                }
            }

            await Persistence.RemoveStaleAudioTracksInLibraryAsync(libraryId, validTrackPaths, cancellationToken);
            processedDirs++;

            if (processedDirs % 10 == 0) {
                await context.ReportProgressAsync(processedDirs * 80 / dirGroups.Count,
                    $"Processed {processedDirs}/{dirGroups.Count} directories", cancellationToken);
            }
        }

        await Persistence.RemoveStaleAudioLibrariesInRootAsync(root.Id, validLibraryPaths, cancellationToken);
    }
}
