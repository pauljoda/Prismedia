using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Books;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Media.Books;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Playback;
using static Prismedia.Infrastructure.Tests.EntityConcurrencyTestSupport;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// PostgreSQL coverage for the listening write path: audio outside every paired chapter still keeps
/// its exact checkpoint without moving the shared cursor, the alignment resumes it exactly and
/// reports the gap instead of guessing a reading position, and removing the track removes it.
/// </summary>
public sealed class BookListeningAlignmentPostgresTests {
    private static readonly DateTimeOffset ReadAt = DateTimeOffset.Parse("2026-09-24T10:00:00Z");
    private static readonly DateTimeOffset ListenedAt = ReadAt.AddMinutes(30);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task UnpairedListeningKeepsItsExactPositionWithoutMovingTheReadingCursor() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var fixture = await SeedAsync(database);

        await using (var db = database.CreateContext()) {
            var reading = await CreateCapabilities(db, fixture, ReadAt).ReportProgressAsync(
                fixture.BookId,
                new EntityProgressReport(
                    fixture.BookId,
                    ProgressUnit.Cfi,
                    2_500,
                    10_000,
                    ReaderMode.Paged,
                    Location: "epubcfi(/6/4!/4/2)",
                    Modality: ConsumptionModality.Reading),
                CancellationToken.None);
            Assert.Equal(EntityProgressReportStatus.Applied, reading.Status);
        }

        await using (var db = database.CreateContext()) {
            var listening = await CreateCapabilities(db, fixture, ListenedAt).ReportProgressAsync(
                fixture.BookId,
                new EntityProgressReport(
                    ActivitySeconds: 15,
                    Modality: ConsumptionModality.Listening,
                    Listening: new ListeningPositionRequest(fixture.UnpairedTrackId, null, 120.5)),
                CancellationToken.None);
            Assert.Equal(EntityProgressReportStatus.Applied, listening.Status);
        }

        await using (var db = database.CreateContext()) {
            var state = await db.UserEntityStates.SingleAsync(row => row.UserId == fixture.UserId && row.EntityId == fixture.BookId);
            Assert.Equal(ProgressUnit.Cfi.ToCode(), state.ProgressUnit);
            Assert.Equal(2_500, state.ProgressIndex);
            Assert.Equal(ReadAt, state.ProgressUpdatedAt);

            var alignment = await CreateAlignments(db, fixture).GetAsync(fixture.BookId, CancellationToken.None);
            var resume = Assert.IsType<BookResumeProjection>(alignment?.Resume);
            Assert.Equal(ConsumptionModality.Listening, resume.LastModality);
            Assert.Equal(AlignmentBasis.Exact, resume.Continue?.Basis);
            Assert.Equal(fixture.UnpairedTrackId, resume.Continue?.Listening?.TrackEntityId);
            Assert.Equal(120.5, resume.Continue?.Listening?.OffsetSeconds);
            Assert.Equal("epubcfi(/6/4!/4/2)", resume.ExactReading?.Location);
            Assert.Equal(AlignmentGapReason.AudioChapterUnpaired, resume.SwitchToReading.Gap);
            Assert.Equal("Part 2", resume.SwitchToReading.GapChapterTitle);
            Assert.Null(resume.SwitchToReading.Reading);
        }

        // Paired audio places the shared cursor at the aligned readable position, without a locator.
        await using (var db = database.CreateContext()) {
            await CreateCapabilities(db, fixture, ListenedAt.AddMinutes(5)).ReportProgressAsync(
                fixture.BookId,
                new EntityProgressReport(
                    Modality: ConsumptionModality.Listening,
                    Listening: new ListeningPositionRequest(fixture.PairedTrackId, null, 450)),
                CancellationToken.None);
            var state = await db.UserEntityStates.AsNoTracking()
                .SingleAsync(row => row.UserId == fixture.UserId && row.EntityId == fixture.BookId);
            Assert.Equal(3_750, state.ProgressIndex);
            Assert.Null(state.ProgressLocation);
        }

        await using (var db = database.CreateContext()) {
            await db.Entities.Where(row => row.Id == fixture.PairedTrackId).ExecuteDeleteAsync();
            var modalities = await db.UserProgressCheckpoints
                .Where(row => row.UserId == fixture.UserId && row.EntityId == fixture.BookId)
                .Select(row => row.Modality)
                .ToArrayAsync();
            Assert.Equal([ConsumptionModality.Reading], modalities);
        }
    }

    private static EntityCapabilityService CreateCapabilities(
        PrismediaDbContext db,
        Fixture fixture,
        DateTimeOffset now) {
        var user = TestUserContext.Admin(fixture.UserId);
        var topology = new EfEntityProgressTopologyResolver(db);
        return new EntityCapabilityService(
            CreateRepository(db, fixture.UserId),
            new EfEntityReadService(db, user, CreateRepository(db, fixture.UserId), ThumbnailContributors.For(db), topology),
            topology,
            consumptionEvents: new EfConsumptionEventStore(db, user),
            timeProvider: new FixedTimeProvider(now),
            consumptionActivities: new EfConsumptionActivityStore(db, user),
            workAlignments: new EfWorkAlignmentReader(db, new FixtureContents()));
    }

    private static BookAlignmentService CreateAlignments(PrismediaDbContext db, Fixture fixture) =>
        new(
            CreateRepository(db, fixture.UserId),
            new EfWorkAlignmentReader(db, new FixtureContents()),
            new EveryEntityVisible());

    private static async Task<Fixture> SeedAsync(PostgresTestDatabase database) {
        var fixture = new Fixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = database.CreateContext();
        db.Users.Add(new UserRow {
            Id = fixture.UserId,
            Username = $"user-{fixture.UserId:N}",
            NormalizedUsername = $"user-{fixture.UserId:N}",
            DisplayName = "Listener",
            Role = UserRole.Admin,
            AllowNsfw = true,
            CanCreateLibraries = true,
            Enabled = true,
            CreatedAt = ReadAt,
            UpdatedAt = ReadAt
        });
        db.Entities.Add(new EntityRow {
            Id = fixture.BookId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Fixture Book",
            CreatedAt = ReadAt,
            UpdatedAt = ReadAt
        });
        db.BookDetails.Add(new BookDetailRow { EntityId = fixture.BookId });
        foreach (var (trackId, title, order) in new[] {
                     (fixture.PairedTrackId, "Part 1", 0),
                     (fixture.UnpairedTrackId, "Part 2", 1)
                 }) {
            db.Entities.Add(new EntityRow {
                Id = trackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = title,
                ParentEntityId = fixture.BookId,
                SortOrder = order,
                CreatedAt = ReadAt,
                UpdatedAt = ReadAt
            });
            db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = trackId,
                Role = EntityFileRole.Source,
                Path = $"/media/fixture/{title}.m4b",
                CreatedAt = ReadAt,
                UpdatedAt = ReadAt
            });
            db.EntityTechnical.Add(new EntityTechnicalRow { EntityId = trackId, DurationSeconds = 600, UpdatedAt = ReadAt });
        }
        db.BookChapterAudioMappings.Add(new BookChapterAudioMappingRow {
            Id = Guid.NewGuid(),
            BookId = fixture.BookId,
            ReadableChapterKey = FixtureContents.FirstChapterKey,
            AudioTrackEntityId = fixture.PairedTrackId,
            Origin = BookChapterMappingOrigin.Manual,
            UpdatedAt = ReadAt
        });
        await db.SaveChangesAsync();
        return fixture;
    }

    private sealed record Fixture(Guid UserId, Guid BookId, Guid PairedTrackId, Guid UnpairedTrackId);

    /// <summary>An EPUB table of contents whose second chapter has no audio.</summary>
    private sealed class FixtureContents : IBookContentsService {
        public const string FirstChapterKey = "Text/one.xhtml";

        public Task<BookContentsResponse?> GetAsync(Guid bookId, CancellationToken cancellationToken) =>
            Task.FromResult<BookContentsResponse?>(new BookContentsResponse([
                new BookContentsEntry(FirstChapterKey, "Chapter One", FirstChapterKey, 0, 0, 0, 0, 0.5),
                new BookContentsEntry("Text/two.xhtml", "Chapter Two", "Text/two.xhtml", 0, 1, 1, 0.5, 1)
            ]));
    }

    private sealed class EveryEntityVisible : IEntityVisibilityChecker {
        public Task<bool> IsVisibleAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
