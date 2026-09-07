using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Audio;
using Prismedia.Infrastructure.Security;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class AudioSourceServiceTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-audio-source-{Guid.NewGuid():N}");

    public AudioSourceServiceTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Theory]
    [InlineData("aac", true)]
    [InlineData("mp3", true)]
    [InlineData("alac", false)]
    [InlineData("ape", false)]
    public async Task MarksBrowserPlayableAudioCodecs(string codec, bool directPlayable) {
        await using var db = CreateContext();
        var trackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var filePath = Path.Combine(_tempDir, "track.m4a");
        await File.WriteAllTextAsync(filePath, "audio-bytes");
        SeedAudioSource(db, trackId, filePath, codec);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var source = await service.GetSourceAsync(trackId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(directPlayable, source.DirectPlayable);
        Assert.Equal(codec, source.Codec);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MediaContentTypes.AudioOpus)]
    public async Task DescribesOggOpusFilesWithTheirContainerContentType(string? storedContentType) {
        await using var db = CreateContext();
        var trackId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var filePath = Path.Combine(_tempDir, "track.opus");
        await File.WriteAllTextAsync(filePath, "ogg-opus-bytes");
        SeedAudioSource(
            db,
            trackId,
            filePath,
            MediaCodecs.Opus,
            container: "ogg",
            mimeType: storedContentType);
        await db.SaveChangesAsync();

        var source = await CreateService(db).GetSourceAsync(trackId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(MediaContentTypes.AudioOggOpus, source.ContentType);
        Assert.True(source.DirectPlayable);
    }

    [Fact]
    public async Task WantedTrackNeverResolvesAsAPlayableAudioSource() {
        await using var db = CreateContext();
        var trackId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var filePath = Path.Combine(_tempDir, "wanted-track.mp3");
        await File.WriteAllTextAsync(filePath, "placeholder-audio-bytes");
        SeedAudioSource(db, trackId, filePath, "mp3", isWanted: true);
        await db.SaveChangesAsync();

        var source = await CreateService(db).GetSourceAsync(trackId, CancellationToken.None);

        Assert.Null(source);
    }

    [Fact]
    public async Task SourceFileDoesNotMakeANonPlayableEntityAnAudioSource() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var filePath = Path.Combine(_tempDir, "book.m4b");
        await File.WriteAllTextAsync(filePath, "audio-bytes");
        db.Entities.Add(new EntityRow {
            Id = bookId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Book queue owner",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = bookId,
            Role = EntityFileRole.Source,
            Path = filePath,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var source = await CreateService(db).GetSourceAsync(bookId, CancellationToken.None);

        Assert.Null(source);
    }

    [Theory]
    [InlineData(true, true, MediaCodecs.Mp3)]
    [InlineData(false, true, MediaCodecs.Mp3)]
    [InlineData(true, false, MediaCodecs.Mp3)]
    [InlineData(false, false, MediaCodecs.Mp3)]
    [InlineData(true, true, MediaCodecs.Ac3)]
    [InlineData(false, true, MediaCodecs.Ac3)]
    [InlineData(true, false, MediaCodecs.Ac3)]
    [InlineData(false, false, MediaCodecs.Ac3)]
    public async Task SourceResolutionEnforcesMemberLibraryAccess(bool granted, bool enabled, string codec) {
        await using var db = CreateContext();
        var trackId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var filePath = Path.Combine(_tempDir, "restricted.m4a");
        await File.WriteAllTextAsync(filePath, "audio-bytes");
        SeedAudioSource(db, trackId, filePath, codec);
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = rootId, Path = _tempDir, Label = "Audio", Enabled = enabled
        });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = trackId, LibraryRootId = rootId });
        await db.SaveChangesAsync();
        var user = granted ? TestUserContext.Member(rootId) : TestUserContext.Member();
        var service = CreateService(db, user);

        var source = await service.GetSourceAsync(trackId, CancellationToken.None);

        Assert.Equal(granted && enabled, source is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NsfwLibraryPlaybackFollowsAccountPermission(bool allowNsfw) {
        await using var db = CreateContext();
        var trackId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var userId = TestUserContext.UserId;
        var filePath = Path.Combine(_tempDir, "nsfw.mp3");
        await File.WriteAllTextAsync(filePath, "audio-bytes");
        SeedAudioSource(db, trackId, filePath, MediaCodecs.Mp3);
        db.Users.Add(new UserRow { Id = userId, AllowNsfw = allowNsfw });
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = rootId, Path = _tempDir, Label = "Restricted audio", Enabled = true, IsNsfw = true
        });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = trackId, LibraryRootId = rootId });
        await db.SaveChangesAsync();
        var access = new EfLibraryAccessReader(db);
        await access.ReplaceUserAccessAsync(userId, [rootId], CancellationToken.None);
        var allowedRoots = await access.GetAllowedRootIdsAsync(userId, CancellationToken.None);
        var service = CreateService(db, TestUserContext.Member(allowedRoots.ToArray()));

        var source = await service.GetSourceAsync(trackId, CancellationToken.None);

        Assert.Equal(allowNsfw, source is not null);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static AudioSourceService CreateService(PrismediaDbContext db, ICurrentUserContext? user = null) {
        user ??= TestUserContext.Admin();
        var repository = new EfEntityRepository(db, user, EntityMappers.Kinds(db, user), EntityMappers.Capabilities(db, user));
        var read = new EfEntityReadService(db, user, repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));
        return new AudioSourceService(db, new EfEntityVisibilityChecker(read));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"audio-source-{Guid.NewGuid():N}")
            .Options);

    private static void SeedAudioSource(
        PrismediaDbContext db,
        Guid trackId,
        string filePath,
        string codec,
        bool isWanted = false,
        string? container = null,
        string? mimeType = null) {
        db.Entities.Add(new EntityRow {
            Id = trackId,
            KindCode = EntityKind.AudioTrack.ToCode(),
            Title = "Track",
            IsWanted = isWanted,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = trackId,
            Role = EntityFileRole.Source,
            Path = filePath,
            MimeType = mimeType,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = trackId,
            Codec = codec,
            Container = container,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
