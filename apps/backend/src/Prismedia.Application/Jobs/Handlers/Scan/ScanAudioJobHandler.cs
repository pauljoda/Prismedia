using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
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
    IScanSnapshotStore? snapshots = null,
    IAcquisitionHintApplier? acquisitionHints = null) : ScanJobHandler(logger, fileDiscovery, roots, snapshots) {
    public override JobType Type => JobType.ScanAudio;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanAudio;

    protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.Audio];

    protected override async Task OnNoFileChangesAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        var settings = await Roots.GetSettingsAsync(cancellationToken);
        if (!root.AutoIdentify) {
            settings = settings with { AutoIdentifyEnabled = false };
        }

        await AutoIdentifyScanEnqueue.EnqueueExistingRootsForRootAsync(
            context, settings, downstreamNeeds, root.Id, ScanCategories, cancellationToken);
        await EnqueueExistingTrackJobsAsync(context, settings, root.Id, cancellationToken);
    }

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

        // Albums, loose tracks, and artist groupings are all auto-identify candidates. The artist
        // identifies for its own metadata/artwork only; each album stays its own auto-identify root.
        var autoIdentifyIds = new List<Guid>();

        // Bind request-created wanted entities to the folders the scan is about to upsert (the album an
        // acquisition imported, and its fileless wanted artist container), so the path-keyed upserts find
        // them instead of creating duplicates. Must run BEFORE the artist/album upserts.
        if (acquisitionHints is not null) {
            foreach (var artist in layout.Artists) {
                await acquisitionHints.BindWantedParentAsync(EntityKind.MusicArtist, artist.Path, cancellationToken);
            }

            foreach (var album in layout.Albums) {
                await acquisitionHints.BindWantedEntityAsync(EntityKind.AudioLibrary, album.Path, cancellationToken);
            }
        }

        // 1. Artist groupings.
        var artistSortOrders = SiblingSortOrders(layout.Artists.Select(artist => artist.Path).ToList());
        var artistIdsByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var artistItems = layout.Artists
            .Select(artist => new MusicArtistUpsertItem(
                artist.Path,
                artist.Title,
                root.Id,
                artistSortOrders[artist.Path],
                root.IsNsfw))
            .ToArray();
        var artistIds = await audio.UpsertMusicArtistsBatchAsync(artistItems, cancellationToken);
        for (var i = 0; i < artistItems.Length && i < artistIds.Count; i++) {
            artistIdsByPath[artistItems[i].FolderPath] = artistIds[i];
            autoIdentifyIds.Add(artistIds[i]);
        }

        // 2. Albums, parented to their artist when one exists.
        var albumSortOrders = SiblingSortOrders(layout.Albums.Select(album => album.Path).ToList());
        var albumIdsByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var albumItems = layout.Albums
            .Select(album => {
                Guid? parentArtistId = album.ArtistPath is not null ? artistIdsByPath[album.ArtistPath] : null;
                return new AudioLibraryUpsertItem(
                    album.Path,
                    album.Title,
                    root.Id,
                    parentArtistId,
                    albumSortOrders[album.Path],
                    root.IsNsfw);
            })
            .ToArray();
        var albumIds = await audio.UpsertAudioLibrariesBatchAsync(albumItems, cancellationToken);
        for (var i = 0; i < albumItems.Length && i < albumIds.Count; i++) {
            albumIdsByPath[albumItems[i].FolderPath] = albumIds[i];
            autoIdentifyIds.Add(albumIds[i]);
        }

        // 3. Tracks, ordered album-global across sections, carrying their section label/order.
        var validTrackPathsByAlbum = layout.Albums.ToDictionary(
            album => album.Path,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var total = Math.Max(1, layout.Albums.Count + 1);
        var trackItems = new List<PreparedAudioTrack>();

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

                    trackItems.Add(new PreparedAudioTrack(
                        new AudioTrackUpsertItem(
                            filePath,
                            title,
                            albumId,
                            globalIndex++,
                            section.Label,
                            section.Order,
                            root.IsNsfw),
                        IsLoose: false));
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

                trackItems.Add(new PreparedAudioTrack(
                    new AudioTrackUpsertItem(
                        filePath,
                        title,
                        AudioLibraryId: null,
                        SortOrder: index++,
                        SectionLabel: null,
                        SectionOrder: 0,
                        IsNsfw: root.IsNsfw),
                    IsLoose: true));
            }
        }

        var trackIds = await audio.UpsertAudioTracksBatchAsync(
            trackItems.Select(item => item.Item).ToArray(),
            cancellationToken);
        for (var i = 0; i < trackItems.Count && i < trackIds.Count; i++) {
            var track = trackItems[i];
            var trackId = trackIds[i];

            await ChainTrackJobsAsync(context, settings, trackId, track.Item.Title, cancellationToken);
            if (track.IsLoose) {
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
        var hasTechnical = await downstreamNeeds.HasEntityTechnicalAsync(trackId, cancellationToken);
        if (settings.AutoGenerateMetadata && !hasTechnical) {
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.ProbeAudio,
                    EntityKind.AudioTrack,
                    trackId.ToString(),
                    title,
                    JobPriorities.Probe),
                cancellationToken);
        }

        if (await FingerprintGating.ShouldFingerprintAsync(downstreamNeeds, settings, trackId, cancellationToken)) {
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.FingerprintAudio,
                    EntityKind.AudioTrack,
                    trackId.ToString(),
                    title,
                    JobPriorities.Fingerprint),
                cancellationToken);
        }

        if (settings.AutoGeneratePreview && hasTechnical &&
            !await downstreamNeeds.HasEntityFileAsync(trackId, EntityFileRole.Waveform, cancellationToken)) {
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.GenerateAudioWaveform,
                    EntityKind.AudioTrack,
                    trackId.ToString(),
                    title,
                    JobPriorities.Waveform),
                cancellationToken);
        }
    }

    private async Task EnqueueExistingTrackJobsAsync(
        JobContext context,
        LibrarySettingsData settings,
        Guid rootId,
        CancellationToken cancellationToken) {
        var tracks = await audio.GetAudioTrackTargetsInRootAsync(rootId, cancellationToken);
        if (tracks.Count == 0) {
            return;
        }

        var needs = await downstreamNeeds.CheckDownstreamNeedsBatchAsync(
            tracks.Select(track => track.Id).ToArray(), cancellationToken);
        var requests = new List<EnqueueJobRequest>();
        foreach (var track in tracks) {
            if (!needs.TryGetValue(track.Id, out var entityNeeds)) {
                continue;
            }

            var id = track.Id.ToString();
            if (settings.AutoGenerateMetadata && entityNeeds.NeedsProbe) {
                requests.Add(EnqueueJobRequest.ForEntity(
                    JobType.ProbeAudio,
                    EntityKind.AudioTrack,
                    id,
                    track.Title,
                    JobPriorities.Probe));
            }

            if (FingerprintGating.ShouldFingerprint(settings, entityNeeds)) {
                requests.Add(EnqueueJobRequest.ForEntity(
                    JobType.FingerprintAudio,
                    EntityKind.AudioTrack,
                    id,
                    track.Title,
                    JobPriorities.Fingerprint));
            }

            if (settings.AutoGeneratePreview && !entityNeeds.NeedsProbe && entityNeeds.NeedsPreview) {
                requests.Add(EnqueueJobRequest.ForEntity(
                    JobType.GenerateAudioWaveform,
                    EntityKind.AudioTrack,
                    id,
                    track.Title,
                    JobPriorities.Waveform));
            }
        }

        if (requests.Count > 0) {
            var enqueued = await context.EnqueueBatchAsync(requests, cancellationToken);
            logger.LogDebug("ScanAudio: enqueued {Enqueued}/{Total} downstream jobs for unchanged tracks",
                enqueued, requests.Count);
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

    private sealed record PreparedAudioTrack(AudioTrackUpsertItem Item, bool IsLoose);
}
