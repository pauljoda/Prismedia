using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers audio files and resolves them into the two supported on-disk music layouts —
/// <c>Album/Songs</c> and <c>Artist/Album/Songs</c> — via <see cref="AudioLibraryClassifier"/>.
/// Disc subfolders inside an album become track sections rather than nested albums, and an
/// artist folder is created as a grouping (<see cref="EntityKind.MusicArtist"/>) for its albums.
/// Track, album, and artist entities are upserted and downstream probe/fingerprint jobs chained.
/// </summary>
public sealed class ScanAudioJobHandler(
    ILogger<ScanAudioJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IAudioScanPersistence audio,
    IDownstreamNeedsPersistence downstreamNeeds,
    IScanSnapshotStore? snapshots = null) : ScanJobHandler(logger, fileDiscovery, roots, snapshots) {
    public override JobType Type => JobType.ScanAudio;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanAudio;

    protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.Audio];

    protected override async Task ScanRootCoreAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanAudio: discovering audio files in {Path}", root.Path);
        var excludedPaths = await Roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);

        var dirGroups = await FileDiscovery.DiscoverFilesByDirectoryAsync(
            root.Path, MediaCategory.Audio, root.Recursive, excludedPaths, cancellationToken);

        var settings = await Roots.GetSettingsAsync(cancellationToken);
        if (!root.AutoIdentify) {
            // Honor this root's Auto Identify opt-out without touching other generation settings.
            settings = settings with { AutoIdentifyEnabled = false };
        }

        // Normalize discovery keys so they line up with the classifier's normalized paths.
        var filesByDirectory = dirGroups.ToDictionary(
            group => NormalizePath(group.Key), group => group.Value, StringComparer.OrdinalIgnoreCase);

        // Resolve the directory tree into artists, albums, and album sections (discs).
        var trackDirectories = filesByDirectory.Keys.Where(path => !SamePath(path, root.Path));
        var layout = AudioLibraryClassifier.Classify(root.Path, trackDirectories);

        logger.LogInformation("ScanAudio: resolved {ArtistCount} artists and {AlbumCount} albums in {Label}",
            layout.Artists.Count, layout.Albums.Count, root.Label);

        // Albums and loose tracks are auto-identify candidates; the artist grouping is identified
        // on demand, so it is excluded from the scan cascade.
        var autoIdentifyIds = new List<Guid>();

        // 1. Artist groupings.
        var artistSortOrders = SiblingSortOrders(layout.Artists.Select(artist => artist.Path).ToList());
        var artistIdsByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var artist in layout.Artists) {
            artistIdsByPath[artist.Path] = await audio.UpsertMusicArtistAsync(
                artist.Path, artist.Title, root.Id, artistSortOrders[artist.Path], root.IsNsfw, cancellationToken);
        }

        // 2. Albums, parented to their artist when one exists.
        var albumSortOrders = SiblingSortOrders(layout.Albums.Select(album => album.Path).ToList());
        var albumIdsByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var album in layout.Albums) {
            Guid? parentArtistId = album.ArtistPath is not null ? artistIdsByPath[album.ArtistPath] : null;
            var albumId = await audio.UpsertAudioLibraryAsync(
                album.Path, album.Title, root.Id, parentArtistId, albumSortOrders[album.Path], root.IsNsfw, cancellationToken);
            albumIdsByPath[album.Path] = albumId;
            autoIdentifyIds.Add(albumId);
        }

        // 3. Tracks, ordered album-global across sections, carrying their section label/order.
        var validTrackPathsByAlbum = layout.Albums.ToDictionary(
            album => album.Path,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var total = Math.Max(1, layout.Albums.Count + 1);

        foreach (var album in layout.Albums) {
            var albumId = albumIdsByPath[album.Path];
            var validTrackPaths = validTrackPathsByAlbum[album.Path];
            var globalIndex = 0;

            foreach (var section in album.Sections) {
                if (!filesByDirectory.TryGetValue(section.DirectoryPath, out var sectionFiles)) {
                    continue;
                }

                foreach (var filePath in sectionFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
                    var title = Path.GetFileNameWithoutExtension(filePath);
                    validTrackPaths.Add(filePath);

                    var trackId = await audio.UpsertAudioTrackAsync(
                        filePath, title, albumId, globalIndex++, section.Label, section.Order, root.IsNsfw, cancellationToken);

                    await ChainTrackJobsAsync(context, settings, trackId, title, cancellationToken);
                }
            }

            processed++;
            if (processed % 10 == 0) {
                await context.ReportProgressAsync(processed * 80 / total,
                    $"Processed {processed}/{layout.Albums.Count} albums", cancellationToken);
            }
        }

        // 4. Loose tracks sitting directly under the root (no album folder).
        var validLooseTrackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (filesByDirectory.TryGetValue(NormalizePath(root.Path), out var looseFiles)) {
            var index = 0;
            foreach (var filePath in looseFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
                var title = Path.GetFileNameWithoutExtension(filePath);
                validLooseTrackPaths.Add(filePath);

                var trackId = await audio.UpsertAudioTrackAsync(
                    filePath, title, audioLibraryId: null, index++, sectionLabel: null, sectionOrder: 0, root.IsNsfw, cancellationToken);

                await ChainTrackJobsAsync(context, settings, trackId, title, cancellationToken);
                autoIdentifyIds.Add(trackId);
            }
        }

        // 5. Reconcile: drop tracks/albums/artists that no longer exist on disk.
        await audio.RemoveStaleLooseAudioTracksInRootAsync(root.Id, validLooseTrackPaths, cancellationToken);
        await Roots.RemoveEntitiesInExcludedPathsAsync(root.Id, cancellationToken);

        foreach (var album in layout.Albums) {
            await audio.RemoveStaleAudioTracksInLibraryAsync(
                albumIdsByPath[album.Path], validTrackPathsByAlbum[album.Path], cancellationToken);
        }

        await audio.RemoveStaleAudioLibrariesInRootAsync(
            root.Id, layout.Albums.Select(album => album.Path).ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);
        await audio.RemoveStaleMusicArtistsInRootAsync(
            root.Id, layout.Artists.Select(artist => artist.Path).ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);

        await AutoIdentifyScanEnqueue.EnqueueRootsAsync(context, settings, downstreamNeeds, autoIdentifyIds, cancellationToken);
    }

    /// <summary>Queues probe and fingerprint jobs for a freshly upserted track when needed.</summary>
    private async Task ChainTrackJobsAsync(
        JobContext context,
        LibrarySettingsData settings,
        Guid trackId,
        string title,
        CancellationToken cancellationToken) {
        if (settings.AutoGenerateMetadata && !await downstreamNeeds.HasEntityTechnicalAsync(trackId, cancellationToken)) {
            await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                JobType.ProbeAudio, TargetEntityKind: "audio-track",
                TargetEntityId: trackId.ToString(), TargetLabel: title, Priority: JobPriorities.Probe), cancellationToken);
        }

        if (await FingerprintGating.ShouldFingerprintAsync(downstreamNeeds, settings, trackId, cancellationToken)) {
            await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                JobType.FingerprintAudio, TargetEntityKind: "audio-track",
                TargetEntityId: trackId.ToString(), TargetLabel: title, Priority: JobPriorities.Fingerprint), cancellationToken);
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static Dictionary<string, int> SiblingSortOrders(IReadOnlyList<string> folderPaths) {
        var sortOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var siblings in folderPaths.GroupBy(path => Path.GetDirectoryName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)) {
            var ordered = siblings
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (var i = 0; i < ordered.Length; i++) {
                sortOrders[ordered[i]] = i;
            }
        }

        return sortOrders;
    }
}
