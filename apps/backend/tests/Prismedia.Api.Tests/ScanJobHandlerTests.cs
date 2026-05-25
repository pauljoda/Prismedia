using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class ScanJobHandlerTests {
    [Fact]
    public async Task VideoScanEnqueuesPreviewJobWhenOnlyTrickplayNeedsGeneration() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateFingerprints: false,
                GeneratePhash: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: true,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2),
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    NeedsFingerprint: false,
                    NeedsPreview: false,
                    NeedsTrickplay: true,
                    NeedsSubtitleExtraction: false)
            }
        };
        var discovery = new RecordingFileDiscovery(["/media/videos/movie.mkv"]);
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.GeneratePreview, request.Type);
        Assert.Equal(videoId.ToString(), request.TargetEntityId);
    }

    [Fact]
    public async Task VideoScanClassifiesSeasonFolderEpisodesForHierarchyMaterialization() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateFingerprints: false,
                GeneratePhash: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2),
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    NeedsFingerprint: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false)
            }
        };
        var discovery = new RecordingFileDiscovery([
            "/media/videos/The Chair Company/Season 1/The Chair Company - S01E02 - New Blood.mkv"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Equal("The Chair Company", item.Series?.Title);
        Assert.Equal("/media/videos/The Chair Company", item.Series?.FolderPath);
        Assert.Equal("Season 1", item.Season?.Title);
        Assert.Equal("/media/videos/The Chair Company/Season 1", item.Season?.FolderPath);
        Assert.Equal(1, item.Season?.SeasonNumber);
        Assert.Equal(2, item.EpisodeNumber);
    }

    [Fact]
    public async Task GalleryScanTreatsRootFilesAsLooseAndFoldersAsNestedGalleries() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/images",
            "Images",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: true,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings
        };
        var discovery = new RecordingFileDiscovery(
            directoryGroups: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["/media/images"] = ["/media/images/root.png"],
                ["/media/images/Gallery"] = ["/media/images/Gallery/a.png"],
                ["/media/images/Gallery/A secondGallery"] = ["/media/images/Gallery/A secondGallery/b.png"]
            });
        var handler = new ScanGalleryJobHandler(
            NullLogger<ScanGalleryJobHandler>.Instance,
            discovery,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanGallery,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.DoesNotContain(persistence.UpsertedGalleries, gallery => gallery.FolderPath == root.Path);
        var gallery = Assert.Single(persistence.UpsertedGalleries, item => item.FolderPath == "/media/images/Gallery");
        var nestedGallery = Assert.Single(persistence.UpsertedGalleries, item => item.FolderPath == "/media/images/Gallery/A secondGallery");
        Assert.Null(gallery.ParentGalleryEntityId);
        Assert.Equal(0, gallery.SortOrder);
        Assert.Equal(gallery.Id, nestedGallery.ParentGalleryEntityId);
        Assert.Equal(0, nestedGallery.SortOrder);

        Assert.Collection(
            persistence.UpsertedImages.OrderBy(image => image.FilePath, StringComparer.OrdinalIgnoreCase),
            image => {
                Assert.Equal("/media/images/Gallery/A secondGallery/b.png", image.FilePath);
                Assert.Equal(nestedGallery.Id, image.GalleryEntityId);
            },
            image => {
                Assert.Equal("/media/images/Gallery/a.png", image.FilePath);
                Assert.Equal(gallery.Id, image.GalleryEntityId);
            },
            image => {
                Assert.Equal("/media/images/root.png", image.FilePath);
                Assert.Null(image.GalleryEntityId);
            });
        Assert.Equal(["/media/images/root.png"], persistence.ValidLooseImagePaths);
        Assert.Equal(["/media/images/Gallery", "/media/images/Gallery/A secondGallery"], persistence.ValidGalleryPaths);
        Assert.Equal(["/media/images/Gallery/a.png"], persistence.ValidImagePathsByGalleryId[gallery.Id]);
        Assert.Equal(["/media/images/Gallery/A secondGallery/b.png"], persistence.ValidImagePathsByGalleryId[nestedGallery.Id]);
    }

    [Fact]
    public async Task AudioScanTreatsRootTracksAsLooseAndFoldersAsNestedLibraries() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/audio",
            "Audio",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: false,
            ScanAudio: true,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings
        };
        var discovery = new RecordingFileDiscovery(
            directoryGroups: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["/media/audio"] = ["/media/audio/root.flac"],
                ["/media/audio/Album"] = ["/media/audio/Album/one.flac"],
                ["/media/audio/Album/Disc 2"] = ["/media/audio/Album/Disc 2/two.flac"]
            });
        var handler = new ScanAudioJobHandler(
            NullLogger<ScanAudioJobHandler>.Instance,
            discovery,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanAudio,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.DoesNotContain(persistence.UpsertedAudioLibraries, library => library.FolderPath == root.Path);
        var album = Assert.Single(persistence.UpsertedAudioLibraries, item => item.FolderPath == "/media/audio/Album");
        var disc = Assert.Single(persistence.UpsertedAudioLibraries, item => item.FolderPath == "/media/audio/Album/Disc 2");
        Assert.Null(album.ParentAudioLibraryEntityId);
        Assert.Equal(0, album.SortOrder);
        Assert.Equal(album.Id, disc.ParentAudioLibraryEntityId);
        Assert.Equal(0, disc.SortOrder);

        Assert.Collection(
            persistence.UpsertedAudioTracks.OrderBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase),
            track => {
                Assert.Equal("/media/audio/Album/Disc 2/two.flac", track.FilePath);
                Assert.Equal(disc.Id, track.AudioLibraryEntityId);
            },
            track => {
                Assert.Equal("/media/audio/Album/one.flac", track.FilePath);
                Assert.Equal(album.Id, track.AudioLibraryEntityId);
            },
            track => {
                Assert.Equal("/media/audio/root.flac", track.FilePath);
                Assert.Null(track.AudioLibraryEntityId);
            });
        Assert.Equal(["/media/audio/root.flac"], persistence.ValidLooseAudioTrackPaths);
        Assert.Equal(["/media/audio/Album", "/media/audio/Album/Disc 2"], persistence.ValidAudioLibraryPaths);
        Assert.Equal(["/media/audio/Album/one.flac"], persistence.ValidAudioTrackPathsByLibraryId[album.Id]);
        Assert.Equal(["/media/audio/Album/Disc 2/two.flac"], persistence.ValidAudioTrackPathsByLibraryId[disc.Id]);
    }

    [Fact]
    public async Task BookScanMaterializesFolderVolumesChaptersAndPages() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-book-scan-");
        try {
            var rootPath = tempRoot.FullName;
            var volumePath = Path.Combine(rootPath, "Promised Neverland", "Volume 01");
            Directory.CreateDirectory(volumePath);
            var chapterOnePath = Path.Combine(volumePath, "Promised Neverland Ch.1.zip");
            var chapterTwoPath = Path.Combine(volumePath, "Promised Neverland Ch.2.zip");
            CreateZip(chapterOnePath, ["002.jpg", "001.jpg"]);
            CreateZip(chapterTwoPath, ["001.jpg"]);

            var root = new LibraryRootData(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                rootPath,
                "Comics",
                Enabled: true,
                Recursive: true,
                ScanVideos: false,
                ScanImages: false,
                ScanAudio: false,
                ScanBooks: true,
                IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) {
                Settings = new LibrarySettingsData(
                    AutoGenerateMetadata: false,
                    AutoGenerateFingerprints: false,
                    GeneratePhash: false,
                    AutoGeneratePreview: false,
                    GenerateTrickplay: false,
                    TrickplayIntervalSeconds: 10,
                    PreviewClipDurationSeconds: 8,
                    ThumbnailQuality: 2,
                    TrickplayQuality: 2)
            };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([chapterTwoPath, chapterOnePath]),
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanBook,
                JobRunStatus.Running,
                Progress: 0,
                Message: null,
                PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
                TargetEntityKind: "library-root",
                TargetEntityId: root.Id.ToString(),
                TargetLabel: root.Label,
                CreatedAt: DateTimeOffset.UtcNow,
                StartedAt: DateTimeOffset.UtcNow,
                FinishedAt: null);

            await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

            var book = Assert.Single(persistence.UpsertedBooks);
            Assert.Equal(Path.Combine(rootPath, "Promised Neverland"), book.SourcePath);
            Assert.Equal("Promised Neverland", book.Title);

            var volume = Assert.Single(persistence.UpsertedBookVolumes);
            Assert.Equal(volumePath, volume.SourcePath);
            Assert.Equal("Volume 01", volume.Title);
            Assert.Equal(book.Id, volume.BookEntityId);
            Assert.Equal(0, volume.SortOrder);

            Assert.Collection(
                persistence.UpsertedBookChapters,
                chapter => {
                    Assert.Equal(chapterOnePath, chapter.SourcePath);
                    Assert.Equal("Promised Neverland Ch.1", chapter.Title);
                    Assert.Equal(volume.Id, chapter.ParentEntityId);
                    Assert.Equal(0, chapter.SortOrder);
                    Assert.Equal(2, chapter.PageCount);
                },
                chapter => {
                    Assert.Equal(chapterTwoPath, chapter.SourcePath);
                    Assert.Equal("Promised Neverland Ch.2", chapter.Title);
                    Assert.Equal(volume.Id, chapter.ParentEntityId);
                    Assert.Equal(1, chapter.SortOrder);
                    Assert.Equal(1, chapter.PageCount);
                });

            Assert.Equal(
                [
                    $"{chapterOnePath}::001.jpg",
                    $"{chapterOnePath}::002.jpg",
                    $"{chapterTwoPath}::001.jpg"
                ],
                persistence.UpsertedBookPages.Select(page => page.SourcePath).ToArray());
            Assert.All(persistence.UpsertedBookPages, page => Assert.Equal(book.Id, page.BookEntityId));
            Assert.Equal([Path.Combine(rootPath, "Promised Neverland")], persistence.ValidBookPaths);
            Assert.Equal([volumePath], persistence.ValidBookVolumePaths);
            Assert.Equal([chapterOnePath, chapterTwoPath], persistence.ValidBookChapterPaths);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HandlesScheduledLibraryRootPayloadAsSingleRootScan() {
        var targetRoot = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/one",
            "Root One",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var otherRoot = targetRoot with {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Path = "/media/two",
            Label = "Root Two"
        };
        var persistence = new FakeScanPersistence([targetRoot, otherRoot]);
        var handler = new RecordingScanHandler(persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{targetRoot.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: targetRoot.Id.ToString(),
            TargetLabel: targetRoot.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);

        Assert.Equal([targetRoot.Id], handler.ScannedRootIds);
        Assert.Equal(targetRoot.Id, persistence.LoadedRootIds.Single());
        Assert.False(persistence.LoadedEnabledRoots);
    }

    private static void CreateZip(string path, IReadOnlyList<string> members) {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var member in members) {
            var entry = archive.CreateEntry(member);
            using var entryStream = entry.Open();
            entryStream.WriteByte(1);
        }
    }

    private static LibrarySettingsData DisabledGeneratedWorkSettings => new(
        AutoGenerateMetadata: false,
        AutoGenerateFingerprints: false,
        GeneratePhash: false,
        AutoGeneratePreview: false,
        GenerateTrickplay: false,
        TrickplayIntervalSeconds: 10,
        PreviewClipDurationSeconds: 8,
        ThumbnailQuality: 2,
        TrickplayQuality: 2);

    private sealed class RecordingScanHandler(FakeScanPersistence persistence)
        : ScanJobHandler(NullLogger<RecordingScanHandler>.Instance, new NoopFileDiscovery(), persistence) {
        public List<Guid> ScannedRootIds { get; } = [];

        public override JobType Type => JobType.ScanLibrary;

        protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanVideos;

        protected override Task ScanRootAsync(
            JobContext context,
            LibraryRootData root,
            CancellationToken cancellationToken) {
            ScannedRootIds.Add(root.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScanPersistence(IReadOnlyList<LibraryRootData> roots) : ILibraryScanPersistence {
        public List<Guid> LoadedRootIds { get; } = [];
        public bool LoadedEnabledRoots { get; private set; }
        public LibrarySettingsData Settings { get; init; } = new(
            AutoGenerateMetadata: true,
            AutoGenerateFingerprints: true,
            GeneratePhash: false,
            AutoGeneratePreview: true,
            GenerateTrickplay: true,
            TrickplayIntervalSeconds: 10,
            PreviewClipDurationSeconds: 8,
            ThumbnailQuality: 2,
            TrickplayQuality: 2);
        public IReadOnlyList<Guid> UpsertedVideoIds { get; init; } = [];
        public IReadOnlyDictionary<Guid, DownstreamNeeds> DownstreamNeedsById { get; init; } =
            new Dictionary<Guid, DownstreamNeeds>();
        public List<VideoUpsertItem> UpsertedVideoItems { get; } = [];
        public List<ImageRecord> UpsertedImages { get; } = [];
        public List<GalleryRecord> UpsertedGalleries { get; } = [];
        public List<AudioTrackRecord> UpsertedAudioTracks { get; } = [];
        public List<AudioLibraryRecord> UpsertedAudioLibraries { get; } = [];
        public List<BookRecord> UpsertedBooks { get; } = [];
        public List<BookVolumeRecord> UpsertedBookVolumes { get; } = [];
        public List<BookChapterRecord> UpsertedBookChapters { get; } = [];
        public List<BookPageRecord> UpsertedBookPages { get; } = [];
        public IReadOnlyList<string> ValidLooseImagePaths { get; private set; } = [];
        public Dictionary<Guid, IReadOnlyList<string>> ValidImagePathsByGalleryId { get; } = [];
        public IReadOnlyList<string> ValidGalleryPaths { get; private set; } = [];
        public IReadOnlyList<string> ValidLooseAudioTrackPaths { get; private set; } = [];
        public Dictionary<Guid, IReadOnlyList<string>> ValidAudioTrackPathsByLibraryId { get; } = [];
        public IReadOnlyList<string> ValidAudioLibraryPaths { get; private set; } = [];
        public IReadOnlyList<string> ValidBookPaths { get; private set; } = [];
        public IReadOnlyList<string> ValidBookVolumePaths { get; private set; } = [];
        public IReadOnlyList<string> ValidBookChapterPaths { get; private set; } = [];
        private readonly Dictionary<string, Guid> _entityIdsBySource = new(StringComparer.OrdinalIgnoreCase);

        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) {
            LoadedRootIds.Add(rootId);
            return Task.FromResult(roots.FirstOrDefault(root => root.Id == rootId));
        }

        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) {
            LoadedEnabledRoots = true;
            return Task.FromResult(roots);
        }

        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);

        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<Guid> UpsertVideoAsync(string filePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid> UpsertImageAsync(string filePath, string title, Guid? galleryEntityId, long? sizeBytes, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"image:{filePath}");
            UpsertedImages.Add(new ImageRecord(id, filePath, title, galleryEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) =>
            UpsertGalleryAsync(folderPath, title, libraryRootId, parentGalleryEntityId: null, sortOrder: 0, isNsfw, cancellationToken);

        public Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentGalleryEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"gallery:{folderPath}");
            UpsertedGalleries.Add(new GalleryRecord(id, folderPath, title, libraryRootId, parentGalleryEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid audioLibraryId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) =>
            UpsertAudioTrackAsync(filePath, title, (Guid?)audioLibraryId, sortOrder, isNsfw, cancellationToken);

        public Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid? audioLibraryId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"audio-track:{filePath}");
            UpsertedAudioTracks.Add(new AudioTrackRecord(id, filePath, title, audioLibraryId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertAudioLibraryAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) =>
            UpsertAudioLibraryAsync(folderPath, title, libraryRootId, parentAudioLibraryEntityId: null, sortOrder: 0, isNsfw, cancellationToken);

        public Task<Guid> UpsertAudioLibraryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentAudioLibraryEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"audio-library:{folderPath}");
            UpsertedAudioLibraries.Add(new AudioLibraryRecord(id, folderPath, title, libraryRootId, parentAudioLibraryEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertBookAsync(string sourcePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book:{sourcePath}");
            UpsertedBooks.Add(new BookRecord(id, sourcePath, title, libraryRootId));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertBookVolumeAsync(string folderPath, string title, Guid bookEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book-volume:{folderPath}");
            UpsertedBookVolumes.Add(new BookVolumeRecord(id, folderPath, title, bookEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertBookChapterAsync(string archivePath, string title, Guid parentEntityId, int sortOrder, int pageCount, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book-chapter:{archivePath}");
            UpsertedBookChapters.Add(new BookChapterRecord(id, archivePath, title, parentEntityId, sortOrder, pageCount));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertBookPageAsync(string filePath, string title, Guid bookEntityId, Guid chapterEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book-page:{filePath}");
            UpsertedBookPages.Add(new BookPageRecord(id, filePath, title, bookEntityId, chapterEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<int> RemoveStaleVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> RemoveStaleLooseImagesInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidLooseImagePaths = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleImagesInGalleryAsync(Guid galleryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidImagePathsByGalleryId[galleryEntityId] = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleGalleriesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
            ValidGalleryPaths = validFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleLooseAudioTracksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidLooseAudioTrackPaths = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleAudioTracksInLibraryAsync(Guid libraryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidAudioTrackPathsByLibraryId[libraryEntityId] = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleAudioLibrariesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
            ValidAudioLibraryPaths = validFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleBookVolumesAsync(Guid bookEntityId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
            ValidBookVolumePaths = validFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleBookChaptersAsync(Guid bookEntityId, IReadOnlySet<string> validArchivePaths, CancellationToken cancellationToken) {
            ValidBookChapterPaths = ValidBookChapterPaths
                .Concat(validArchivePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleBooksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidBookPaths = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(IReadOnlyList<VideoUpsertItem> items, CancellationToken cancellationToken) {
            UpsertedVideoItems.AddRange(items);
            return Task.FromResult(UpsertedVideoIds);
        }

        public Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) =>
            Task.FromResult(DownstreamNeedsById);

        public Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertEntityTechnicalAsync(Guid entityId, double? duration, int? width, int? height, double? frameRate, int? bitRate, int? sampleRate, int? channels, string? codec, string? container, string? format, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertMediaSourceAsync(Guid entityId, string path, MediaSourceProbeData source, IReadOnlyList<MediaStreamProbeData> streams, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertTrickplayInfoAsync(Guid entityId, TrickplayInfoData info, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path, string? mimeType, long? sizeBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, string value, Guid? entityFileId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format, EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EntityRefreshTarget>> GetEntityTreeAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Guid IdFor(string key) {
            if (_entityIdsBySource.TryGetValue(key, out var id)) {
                return id;
            }

            id = Guid.NewGuid();
            _entityIdsBySource[key] = id;
            return id;
        }
    }

    private sealed record ImageRecord(Guid Id, string FilePath, string Title, Guid? GalleryEntityId, int SortOrder);
    private sealed record GalleryRecord(Guid Id, string FolderPath, string Title, Guid LibraryRootId, Guid? ParentGalleryEntityId, int SortOrder);
    private sealed record AudioTrackRecord(Guid Id, string FilePath, string Title, Guid? AudioLibraryEntityId, int SortOrder);
    private sealed record AudioLibraryRecord(Guid Id, string FolderPath, string Title, Guid LibraryRootId, Guid? ParentAudioLibraryEntityId, int SortOrder);
    private sealed record BookRecord(Guid Id, string SourcePath, string Title, Guid LibraryRootId);
    private sealed record BookVolumeRecord(Guid Id, string SourcePath, string Title, Guid BookEntityId, int SortOrder);
    private sealed record BookChapterRecord(Guid Id, string SourcePath, string Title, Guid ParentEntityId, int SortOrder, int PageCount);
    private sealed record BookPageRecord(Guid Id, string SourcePath, string Title, Guid BookEntityId, Guid ChapterEntityId, int SortOrder);

    private sealed class NoopFileDiscovery : IFileDiscovery {
        public Task<IReadOnlyList<string>> DiscoverFilesAsync(string rootPath, MediaCategory category, bool recursive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(string rootPath, MediaCategory category, bool recursive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingFileDiscovery(
        IReadOnlyList<string>? files = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? directoryGroups = null) : IFileDiscovery {
        public Task<IReadOnlyList<string>> DiscoverFilesAsync(
            string rootPath, MediaCategory category, bool recursive, CancellationToken cancellationToken) =>
            Task.FromResult(files ?? []);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
            string rootPath, MediaCategory category, bool recursive, CancellationToken cancellationToken) {
            if (directoryGroups is not null) {
                return Task.FromResult(directoryGroups);
            }

            var grouped = (files ?? [])
                .GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(grouped);
        }
    }

    private sealed class NoopJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) => Task.FromResult<JobRunSnapshot?>(null);
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobQueueCount>>([]);
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) {
            Enqueued.AddRange(requests);
            return Task.FromResult(requests.Count);
        }
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) => Task.FromResult<JobRunSnapshot?>(null);
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobQueueCount>>([]);
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
