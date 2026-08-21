using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityProgressTopologyResolverTests {
    [Fact]
    public async Task NestedWorkCursorBelongsOnlyToItsNearestBookOwner() {
        await using var db = CreateContext();
        var outerBookId = Guid.NewGuid();
        var innerBookId = Guid.NewGuid();
        var volumeId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            Row(outerBookId, EntityKind.Book, now),
            Row(innerBookId, EntityKind.Book, now, outerBookId),
            Row(volumeId, EntityKind.BookVolume, now, innerBookId),
            Row(chapterId, EntityKind.BookChapter, now, volumeId),
            Row(pageId, EntityKind.BookPage, now, chapterId));
        await db.SaveChangesAsync();

        var resolver = new EfEntityProgressTopologyResolver(db);

        Assert.Null(await resolver.ResolveCursorAsync(outerBookId, pageId, CancellationToken.None));
        Assert.Equal(
            new ProgressOwnerResolution(innerBookId),
            await resolver.ResolveOwnerAsync(chapterId, CancellationToken.None));
        Assert.Null(await resolver.ResolveWorkPositionAsync(
            innerBookId, volumeId, 0, 1, CancellationToken.None));
        Assert.Equal(
            new ProgressWorkPosition(chapterId, 0, 1),
            await resolver.ResolveWorkPositionAsync(innerBookId, pageId, 0, 1, CancellationToken.None));
    }

    [Fact]
    public async Task AudioTrackFallsBackToItselfUnlessItBelongsToABook() {
        await using var db = CreateContext();
        var libraryId = Guid.NewGuid();
        var libraryTrackId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var bookTrackId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            Row(libraryId, EntityKind.AudioLibrary, now),
            Row(libraryTrackId, EntityKind.AudioTrack, now, libraryId),
            Row(bookId, EntityKind.Book, now),
            Row(bookTrackId, EntityKind.AudioTrack, now, bookId));
        await db.SaveChangesAsync();

        var resolver = new EfEntityProgressTopologyResolver(db);

        Assert.Equal(
            new ProgressOwnerResolution(libraryTrackId),
            await resolver.ResolveOwnerAsync(libraryTrackId, CancellationToken.None));
        Assert.Equal(
            new ProgressCursorResolution(libraryTrackId, libraryTrackId),
            await resolver.ResolveCursorAsync(libraryTrackId, libraryTrackId, CancellationToken.None));
        Assert.Equal(
            new ProgressOwnerResolution(bookId),
            await resolver.ResolveOwnerAsync(bookTrackId, CancellationToken.None));
        Assert.Equal(
            new ProgressCursorResolution(bookTrackId, bookTrackId),
            await resolver.ResolveCursorAsync(bookId, bookTrackId, CancellationToken.None));
    }

    [Fact]
    public async Task EpisodeRollupsKeepDirectAndSeasonSequencesSeparate() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var directEpisodeId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var seasonEpisodeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            Row(seriesId, EntityKind.VideoSeries, now),
            Row(directEpisodeId, EntityKind.VideoEpisode, now, seriesId, 0),
            Row(seasonId, EntityKind.VideoSeason, now, seriesId, 1),
            Row(seasonEpisodeId, EntityKind.VideoEpisode, now, seasonId, 0));
        await db.SaveChangesAsync();

        var resolver = new EfEntityProgressTopologyResolver(db);

        Assert.Equal(
            [new OrderedProgressScope(seriesId, directEpisodeId, 0, 1, null)],
            await resolver.ResolveOrderedScopesAsync(directEpisodeId, CancellationToken.None));
        Assert.Equal(
            [
                new OrderedProgressScope(seasonId, seasonEpisodeId, 0, 1, null),
                new OrderedProgressScope(seriesId, seasonEpisodeId, 0, 1, null)
            ],
            await resolver.ResolveOrderedScopesAsync(seasonEpisodeId, CancellationToken.None));
    }

    [Fact]
    public async Task ComicInstallmentsRollUpThroughTheSameDirectAndGroupedSequenceRules() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var directInstallmentId = Guid.NewGuid();
        var volumeId = Guid.NewGuid();
        var firstVolumeInstallmentId = Guid.NewGuid();
        var secondVolumeInstallmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            Row(seriesId, EntityKind.ComicSeries, now),
            Row(directInstallmentId, EntityKind.ComicInstallment, now, seriesId, 0),
            Row(volumeId, EntityKind.ComicVolume, now, seriesId, 1),
            Row(firstVolumeInstallmentId, EntityKind.ComicInstallment, now, volumeId, 0),
            Row(secondVolumeInstallmentId, EntityKind.ComicInstallment, now, volumeId, 1));
        await db.SaveChangesAsync();

        var resolver = new EfEntityProgressTopologyResolver(db);

        Assert.Equal(
            [new OrderedProgressScope(seriesId, directInstallmentId, 0, 1, null)],
            await resolver.ResolveOrderedScopesAsync(directInstallmentId, CancellationToken.None));
        Assert.Equal(
            [
                new OrderedProgressScope(
                    volumeId,
                    firstVolumeInstallmentId,
                    0,
                    2,
                    secondVolumeInstallmentId),
                new OrderedProgressScope(
                    seriesId,
                    firstVolumeInstallmentId,
                    0,
                    2,
                    secondVolumeInstallmentId)
            ],
            await resolver.ResolveOrderedScopesAsync(firstVolumeInstallmentId, CancellationToken.None));
    }

    [Theory]
    [InlineData(EntityKind.Movie)]
    [InlineData(EntityKind.Video)]
    public async Task StandalonePlayableVideosDoNotResolveOrderedScopes(EntityKind kind) {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(Row(entityId, kind, now));
        await db.SaveChangesAsync();

        var scopes = await new EfEntityProgressTopologyResolver(db)
            .ResolveOrderedScopesAsync(entityId, CancellationToken.None);

        Assert.Empty(scopes);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EntityRow Row(
        Guid id,
        EntityKind kind,
        DateTimeOffset now,
        Guid? parentId = null,
        int? sortOrder = null) =>
        new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = kind.ToCode(),
            ParentEntityId = parentId,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
}
