using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfAcquisitionStoreTests {
    [Fact]
    public async Task DownloadedCompletionWorkProjectsKindAndUpgradeRoutingOnlyForDownloadedRows() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var ordinaryId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var upgradeId = Guid.NewGuid();
        var importingId = Guid.NewGuid();
        db.Acquisitions.AddRange(
            CompletionRow(ordinaryId, EntityKind.Movie, AcquisitionStatus.Downloaded),
            CompletionRow(parentId, EntityKind.Book, AcquisitionStatus.Imported),
            CompletionRow(upgradeId, EntityKind.Video, AcquisitionStatus.Downloaded, parentId),
            CompletionRow(importingId, EntityKind.AudioLibrary, AcquisitionStatus.Importing));
        await db.SaveChangesAsync();

        var work = await AcquisitionTestFactory.Store(db)
            .ListDownloadedCompletionWorkAsync(CancellationToken.None);

        Assert.Equal(2, work.Count);
        AssertCompletion(
            Assert.Single(work, item => item.AcquisitionId == ordinaryId),
            ordinaryId,
            EntityKind.Movie,
            isUpgrade: false);
        AssertCompletion(
            Assert.Single(work, item => item.AcquisitionId == upgradeId),
            upgradeId,
            EntityKind.Video,
            isUpgrade: true);

        AcquisitionRow CompletionRow(
            Guid id,
            EntityKind kind,
            AcquisitionStatus status,
            Guid? upgradeOfAcquisitionId = null) => new() {
                Id = id,
                Kind = kind,
                Status = status,
                UpgradeOfAcquisitionId = upgradeOfAcquisitionId,
                Title = kind.ToCode(),
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now,
            };

        static void AssertCompletion(
            DownloadedAcquisitionCompletion item,
            Guid id,
            EntityKind kind,
            bool isUpgrade) {
            Assert.Equal(id, item.AcquisitionId);
            Assert.Equal(kind, item.Kind);
            Assert.Equal(isUpgrade, item.IsUpgrade);
        }
    }

    [Fact]
    public async Task UnsupportedDownloadedWorkDoesNotKeepTransferPollingAlive() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Kind = EntityKind.Image,
            Status = AcquisitionStatus.Downloaded,
            Title = "Unsupported image payload",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "completed-image",
            Progress = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        Assert.Empty(await store.ListActiveTransfersAsync(CancellationToken.None));
        Assert.False(await store.HasActiveTransfersAsync(CancellationToken.None));
        Assert.Equal(
            acquisitionId,
            Assert.Single(await store.ListDownloadedCompletionWorkAsync(CancellationToken.None)).AcquisitionId);
    }

    [Fact]
    public async Task EntityLifecycleIdsIncludeEveryUpgradeDescendant() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var entityId = Guid.NewGuid();
        var directNewest = Guid.NewGuid();
        var directOlder = Guid.NewGuid();
        var upgradeChild = Guid.NewGuid();
        var nestedUpgrade = Guid.NewGuid();
        db.Acquisitions.AddRange(
            Row(directOlder, now.AddHours(-2), entityId: entityId),
            Row(directNewest, now.AddHours(-1), entityId: entityId),
            Row(upgradeChild, now, upgradeOf: directOlder),
            Row(nestedUpgrade, now.AddMinutes(1), upgradeOf: upgradeChild));
        await db.SaveChangesAsync();

        var ids = await AcquisitionTestFactory.Store(db)
            .ListIdsForEntityAsync(entityId, CancellationToken.None);

        Assert.Equal([directNewest, directOlder, upgradeChild, nestedUpgrade], ids);

        static AcquisitionRow Row(
            Guid id,
            DateTimeOffset createdAt,
            Guid? entityId = null,
            Guid? upgradeOf = null) => new() {
                Id = id,
                EntityId = entityId,
                UpgradeOfAcquisitionId = upgradeOf,
                Kind = EntityKind.Movie,
                Status = AcquisitionStatus.Imported,
                Title = "Movie",
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };
    }

    [Fact]
    public async Task FailedRecoveryClaimRequiresTheActiveReleaseAndLifecycle() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var selected = new SelectedRelease("Dune release", "Indexer", "hash-1");
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Downloading,
            Title = "Dune",
            SelectedReleaseJson = System.Text.Json.JsonSerializer.Serialize(selected),
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        Assert.False(await store.TryClaimFailedRecoveryAsync(
            acquisitionId,
            [AcquisitionStatus.Queued, AcquisitionStatus.Downloading],
            selected with { Title = "Different release" },
            "Failed.",
            CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Downloading, await store.GetStatusAsync(acquisitionId, CancellationToken.None));

        Assert.True(await store.TryClaimFailedRecoveryAsync(
            acquisitionId,
            [AcquisitionStatus.Queued, AcquisitionStatus.Downloading],
            selected,
            "Failed.",
            CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Failed, await store.GetStatusAsync(acquisitionId, CancellationToken.None));

        Assert.True(await store.TryTransitionStatusAsync(
            acquisitionId,
            [AcquisitionStatus.Failed],
            AcquisitionStatus.Cancelled,
            "Cancelled.",
            CancellationToken.None));
        Assert.False(await store.TryClaimFailedRecoveryAsync(
            acquisitionId,
            [AcquisitionStatus.Queued, AcquisitionStatus.Downloading, AcquisitionStatus.Failed],
            selected,
            "Failed again.",
            CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Cancelled, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Fact]
    public async Task SearchCompletionCannotReplaceCandidatesAfterCancellation() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Cancelled,
            Title = "Dune",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        AddCandidate(db, acquisitionId, "old-hash", "Old indexer", "Old release", 1);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        var completed = await store.TryCompleteSearchAsync(
            acquisitionId,
            [Scored("New release", "new-hash")],
            "1 acceptable release.",
            CancellationToken.None);

        Assert.False(completed);
        Assert.Equal(AcquisitionStatus.Cancelled, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
        var candidate = Assert.Single((await store.GetAsync(acquisitionId, CancellationToken.None))!.Candidates);
        Assert.Equal("Old release", candidate.Title);
    }

    [Fact]
    public async Task SearchCompletionReplacesCandidatesAndStatusAsOneLifecycleCommit() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Searching,
            Title = "Dune",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        AddCandidate(db, acquisitionId, "old-hash", "Old indexer", "Old release", 1);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        var completed = await store.TryCompleteSearchAsync(
            acquisitionId,
            [Scored("New release", "new-hash")],
            "1 acceptable release.",
            CancellationToken.None);

        Assert.True(completed);
        var detail = (await store.GetAsync(acquisitionId, CancellationToken.None))!;
        Assert.Equal(AcquisitionStatus.AwaitingSelection, detail.Summary.Status);
        Assert.Equal("1 acceptable release.", detail.Summary.StatusMessage);
        var candidate = Assert.Single(detail.Candidates);
        Assert.Equal("New release", candidate.Title);
    }

    [Fact]
    public async Task OpenWorkPredicateIgnoresTerminalHistoryButFindsAnActionableAttempt() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = Guid.NewGuid(), EntityId = entityId, Status = AcquisitionStatus.Imported,
            Title = "Imported history", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            CreatedAt = now, UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = Guid.NewGuid(), EntityId = entityId, Status = AcquisitionStatus.Cancelled,
            Title = "Cancelled history", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            CreatedAt = now.AddSeconds(1), UpdatedAt = now.AddSeconds(1)
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        Assert.False(await store.AnyOpenForEntityAsync(entityId, CancellationToken.None));

        db.Acquisitions.Add(new AcquisitionRow {
            Id = Guid.NewGuid(), EntityId = entityId, Status = AcquisitionStatus.Failed,
            Title = "Open retry", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            CreatedAt = now.AddMinutes(1), UpdatedAt = now.AddMinutes(1)
        });
        await db.SaveChangesAsync();

        Assert.True(await store.AnyOpenForEntityAsync(entityId, CancellationToken.None));
    }

    [Fact]
    public async Task MarkCandidatesBlocklistedRejectsTheMatchingCandidate() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        AddCandidate(db, acquisitionId, infoHash: "hash", indexer: "Indexer", title: "Some Book (epub)", score: 100);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        await store.MarkCandidatesBlocklistedAsync(acquisitionId, ReleaseIdentity.For("hash", "Indexer", "Some Book (epub)"), CancellationToken.None);

        var candidate = Assert.Single((await store.GetAsync(acquisitionId, CancellationToken.None))!.Candidates);
        Assert.False(candidate.Accepted);
        Assert.Contains(ReleaseRejectionReason.Blocklisted, candidate.Rejections);
    }

    [Fact]
    public async Task MarkCandidatesBlocklistedMarksDuplicateRowsForTheSameRelease() {
        // Two indexers returned the same torrent (same info hash). Blocklisting one must reject both,
        // so a duplicate doesn't stay selectable only to be refused at queue time.
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        AddCandidate(db, acquisitionId, infoHash: "DUP", indexer: "Indexer A", title: "Some Book A", score: 100);
        AddCandidate(db, acquisitionId, infoHash: "dup", indexer: "Indexer B", title: "Some Book B", score: 50);
        AddCandidate(db, acquisitionId, infoHash: "other", indexer: "Indexer C", title: "Different Book", score: 10);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        // Identity is info-hash-first and case-insensitive, so "DUP"/"dup" collapse to the same identity.
        await store.MarkCandidatesBlocklistedAsync(acquisitionId, ReleaseIdentity.For("dup", null, null), CancellationToken.None);

        var candidates = (await store.GetAsync(acquisitionId, CancellationToken.None))!.Candidates;
        Assert.Equal(2, candidates.Count(c => !c.Accepted && c.Rejections.Contains(ReleaseRejectionReason.Blocklisted)));
        Assert.Single(candidates, c => c.Accepted); // the unrelated release stays selectable
    }

    [Fact]
    public async Task MarkCandidatesBlocklistedIsIdempotentOnTheReason() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        AddCandidate(db, acquisitionId, infoHash: null, indexer: "Indexer", title: "Some Book", score: 1);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var identity = ReleaseIdentity.For(null, "Indexer", "Some Book");

        await store.MarkCandidatesBlocklistedAsync(acquisitionId, identity, CancellationToken.None);
        await store.MarkCandidatesBlocklistedAsync(acquisitionId, identity, CancellationToken.None);

        var candidate = Assert.Single((await store.GetAsync(acquisitionId, CancellationToken.None))!.Candidates);
        Assert.Equal([ReleaseRejectionReason.Blocklisted], candidate.Rejections);
    }

    [Fact]
    public async Task MarkImportedWithQualityCapturesQualityAtomically() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow { Id = id, Status = AcquisitionStatus.Importing, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        await store.MarkImportedWithQualityAsync(id, new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Reflowable), "Imported.", CancellationToken.None);

        var row = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.Equal(AcquisitionStatus.Imported, row.Status);
        Assert.Equal(BookSourceTier.Retail, row.OwnedSourceTier);
        Assert.Equal(BookFormatTier.Reflowable, row.OwnedFormatTier);
        Assert.True(row.UpgradeQualityCaptured);
    }

    [Fact]
    public async Task MalformedSelectedReleaseMetadataCannotDowngradeATerminalImport() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id,
            Status = AcquisitionStatus.Importing,
            Title = "Show",
            SelectedReleaseJson = "{not-valid-json",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        await store.MarkImportedWithQualityAsync(
            id,
            BookQualityRank.Floor,
            "Imported.",
            CancellationToken.None,
            VideoQuality.Webdl1080p.ToCode());

        Assert.Equal(AcquisitionStatus.Imported, await store.GetStatusAsync(id, CancellationToken.None));
        Assert.Contains(await db.AcquisitionHistory.AsNoTracking().ToArrayAsync(), entry =>
            entry.Event == AcquisitionHistoryEvent.Imported && entry.AcquisitionId == id);
    }

    [Fact]
    public async Task CloneForRetryPreservesSearchIdentityButDropsImportedAndTransferState() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var entityId = AddWantedEntity(db, EntityKindRegistry.VideoSeason.Code, "Season 2");
        var acquisitionId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = entityId,
            ProfileId = profileId,
            TargetLibraryRootId = rootId,
            Kind = EntityKind.VideoSeason,
            Status = AcquisitionStatus.Imported,
            StatusMessage = "Imported.",
            Title = "Season 2",
            Series = "Andor",
            SeasonNumber = 2,
            Year = 2025,
            PosterUrl = "https://example.test/andor.jpg",
            Description = "The second season.",
            IdentityNamespace = "tmdb",
            IdentityValue = "83867:season:2",
            ExternalIdsJson = "{\"tmdb\":\"83867:season:2\"}",
            SourceUrlsJson = "[\"https://example.test/andor\"]",
            SelectedReleaseJson = "{\"title\":\"Andor S02\"}",
            FinalSourcePath = "/media/tv/Andor/Season 02",
            OwnedSourceTier = BookSourceTier.Retail,
            OwnedFormatTier = BookFormatTier.Reflowable,
            OwnedMediaQuality = VideoQuality.Bluray1080p.ToCode(),
            OwnedMediaRevision = 2,
            OwnedFormatScore = 100,
            UpgradeQualityCaptured = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        AddCandidate(db, acquisitionId, "hash", "Indexer", "Andor S02", 100);
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, ClientItemId = "hash",
            CreatedAt = now, UpdatedAt = now
        });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, EntityId = entityId,
            SourcePath = "/media/tv/Andor/Season 02", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var cloneId = await AcquisitionTestFactory.Store(db).CloneForRetryAsync(acquisitionId, CancellationToken.None);

        Assert.NotNull(cloneId);
        var clone = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == cloneId);
        Assert.Equal(AcquisitionStatus.Pending, clone.Status);
        Assert.Equal((EntityKind.VideoSeason, entityId, profileId, rootId),
            (clone.Kind, clone.EntityId, clone.ProfileId, clone.TargetLibraryRootId));
        Assert.Equal(("Season 2", "Andor", 2, 2025),
            (clone.Title, clone.Series, clone.SeasonNumber, clone.Year));
        Assert.Equal(("tmdb", "83867:season:2"), (clone.IdentityNamespace, clone.IdentityValue));
        Assert.Equal("The second season.", clone.Description);
        Assert.Null(clone.SelectedReleaseJson);
        Assert.Null(clone.FinalSourcePath);
        Assert.Equal(BookSourceTier.Unknown, clone.OwnedSourceTier);
        Assert.Equal(BookFormatTier.Unknown, clone.OwnedFormatTier);
        Assert.Null(clone.OwnedMediaQuality);
        Assert.Equal(1, clone.OwnedMediaRevision);
        Assert.Equal(0, clone.OwnedFormatScore);
        Assert.False(clone.UpgradeQualityCaptured);
        Assert.False(await db.ReleaseCandidates.AnyAsync(row => row.AcquisitionId == cloneId));
        Assert.False(await db.DownloadTransfers.AnyAsync(row => row.AcquisitionId == cloneId));
        Assert.False(await db.AcquisitionImportHints.AnyAsync(row => row.AcquisitionId == cloneId));
    }

    [Fact]
    public async Task ReacquireTeardownCreatesAndReturnsOneDurablyLinkedReplacement() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var entityId = AddWantedEntity(db, EntityKindRegistry.Movie.Code, "Arrival");
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = entityId,
            Kind = EntityKind.Movie,
            Status = AcquisitionStatus.Stopping,
            TeardownIntent = AcquisitionTeardownIntent.Reacquire,
            TeardownOriginalStatus = AcquisitionStatus.Imported,
            Title = "Arrival",
            IdentityNamespace = "tmdb",
            IdentityValue = "329865",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        var first = await store.GetOrCreateTeardownReplacementAsync(acquisitionId, CancellationToken.None);
        var second = await store.GetOrCreateTeardownReplacementAsync(acquisitionId, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        var owner = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == acquisitionId);
        Assert.Equal(first, owner.TeardownReplacementAcquisitionId);
        Assert.Equal(2, await db.Acquisitions.CountAsync());
        Assert.Equal(
            AcquisitionStatus.Pending,
            (await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == first)).Status);
        Assert.True(db.Model.FindEntityType(typeof(AcquisitionRow))!
            .FindProperty(nameof(AcquisitionRow.TeardownReplacementAcquisitionId))!
            .IsConcurrencyToken);
    }

    [Fact]
    public async Task TransferPersistenceCannotReplaceATeardownOwnedPointer() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Stopping,
            TeardownIntent = AcquisitionTeardownIntent.Remove,
            TeardownOriginalStatus = AcquisitionStatus.Queued,
            Title = "Arrival",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "superseded-item",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        var queued = await store.CreateTransferAsync(
            acquisitionId,
            clientId,
            "accepted-after-claim",
            "prismedia",
            CancellationToken.None);

        Assert.False(queued);
        var pointer = await db.DownloadTransfers.AsNoTracking().SingleAsync();
        Assert.Equal(acquisitionId, pointer.AcquisitionId);
        Assert.Null(pointer.DownloadClientConfigId);
        Assert.Equal("superseded-item", pointer.ClientItemId);
        Assert.Equal(AcquisitionStatus.Stopping, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Fact]
    public async Task DurableAddPlaceholderIsIdempotentAndExcludedFromNormalTransferPolling() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Queued,
            Title = "Dune",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        Assert.True(await store.BeginTransferAddAsync(
            acquisitionId, clientId, "abc123", "prismedia", null, CancellationToken.None));
        Assert.True(await store.BeginTransferAddAsync(
            acquisitionId, clientId, "abc123", "prismedia", null, CancellationToken.None));

        var placeholder = await db.DownloadTransfers.AsNoTracking().SingleAsync();
        Assert.Equal(TransferOwnershipState.Adding.ToCode(), placeholder.State);
        Assert.Equal("abc123", placeholder.ClientItemId);
        Assert.Empty(await store.ListActiveTransfersAsync(CancellationToken.None));
        Assert.False(await store.HasActiveTransfersAsync(CancellationToken.None));

        Assert.True(await store.CompleteTransferAddAsync(
            acquisitionId,
            clientId,
            "abc123",
            "native-id",
            new SelectedRelease("Dune release", "Indexer", "abc123"),
            "Sent to download client.",
            CancellationToken.None));
        Assert.Equal("native-id", (await store.ListActiveTransfersAsync(CancellationToken.None)).Single().ClientItemId);
        Assert.True(await store.HasActiveTransfersAsync(CancellationToken.None));
        Assert.Equal("Dune release", (await store.GetSelectedReleaseAsync(acquisitionId, CancellationToken.None))?.Title);
        var detail = (await store.GetAsync(acquisitionId, CancellationToken.None))!;
        Assert.Equal(AcquisitionStatus.Queued, detail.Summary.Status);
        Assert.Equal("Sent to download client.", detail.Summary.StatusMessage);
    }

    [Fact]
    public async Task EnrichMetadataFillsGapsWithoutClobbering() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id, Status = AcquisitionStatus.Pending, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            PosterUrl = null, Year = null, Description = null, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        await store.EnrichMetadataAsync(id, "a provider description", "http://cover", 2024, CancellationToken.None);
        var row = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.Equal("http://cover", row.PosterUrl);   // gap filled
        Assert.Equal(2024, row.Year);                  // gap filled
        Assert.Equal("a provider description", row.Description); // gap filled

        // A second enrichment must not clobber anything now set (gap-only on every field, including description).
        await store.EnrichMetadataAsync(id, "a different, longer provider description", "http://other-cover", 1999, CancellationToken.None);
        var row2 = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.Equal("http://cover", row2.PosterUrl);
        Assert.Equal(2024, row2.Year);
        Assert.Equal("a provider description", row2.Description);
    }

    [Fact]
    public async Task HintApplierDoesNotSeedTheEntityDescription() {
        // The entity description is owned by embedded file metadata + auto-identify; the hint applier must not
        // pre-empt them by seeding the request-time description onto the book.
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKindRegistry.Book.Code,
            Title = "Book",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow { Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        db.BookDetails.Add(new BookDetailRow { EntityId = entityId, Format = BookFormat.Epub });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, SourcePath = "/media/books/Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            Description = "a request-time description", Consumed = false, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await new AcquisitionHintApplier(db).ApplyAsync(entityId, "/media/books/Book/Title.epub", CancellationToken.None);

        Assert.False(await db.EntityDescriptions.AsNoTracking().AnyAsync(d => d.EntityId == entityId));
        Assert.True((await db.AcquisitionImportHints.AsNoTracking().FirstAsync()).Consumed); // hint still applied (ids/tier)
    }

    [Fact]
    public async Task HintApplierStampsOwnedSourceTierOnTheBook() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKindRegistry.Book.Code,
            Title = "Book",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow { Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        db.BookDetails.Add(new BookDetailRow { EntityId = entityId, Format = BookFormat.Epub, SourceTier = BookSourceTier.Unknown });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, SourcePath = "/media/books/Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            OwnedSourceTier = BookSourceTier.Retail, OwnedFormatTier = BookFormatTier.Reflowable, Consumed = false, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var applied = await new AcquisitionHintApplier(db).ApplyAsync(entityId, "/media/books/Book/Title.epub", CancellationToken.None);

        Assert.True(applied);
        Assert.Equal(BookSourceTier.Retail, (await db.BookDetails.AsNoTracking().FirstAsync(d => d.EntityId == entityId)).SourceTier);
        Assert.True((await db.AcquisitionImportHints.AsNoTracking().FirstAsync()).Consumed);
    }

    [Fact]
    public async Task BindWantedBookAttachesTheImportedPathAndClearsWanted() {
        await using var db = CreateContext();
        var entityId = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Elantris");
        AddHintWithEntity(db, entityId, "/media/books/Brandon Sanderson/Elantris (2005)/Elantris.epub");
        await db.SaveChangesAsync();

        var bound = await new AcquisitionHintApplier(db).BindWantedEntityAsync(EntityKind.Book,
            "/media/books/Brandon Sanderson/Elantris (2005)/Elantris.epub", CancellationToken.None);

        Assert.True(bound);
        var entity = await db.Entities.AsNoTracking().FirstAsync(row => row.Id == entityId);
        Assert.False(entity.IsWanted);
        var file = Assert.Single(await db.EntityFiles.AsNoTracking().Where(f => f.EntityId == entityId).ToArrayAsync());
        Assert.Equal(EntityFileRole.Source, file.Role);
        // Written exactly as the scan keys it, so the path-keyed upsert finds this entity (no duplicate).
        Assert.Equal("/media/books/Brandon Sanderson/Elantris (2005)/Elantris.epub", file.Path);
        Assert.Equal(Prismedia.Contracts.Media.MediaContentTypes.Epub, file.MimeType);
        // The hint stays unconsumed: the ordinary post-upsert apply still stamps ids and the source tier.
        Assert.False((await db.AcquisitionImportHints.AsNoTracking().FirstAsync()).Consumed);
    }

    [Fact]
    public async Task BindWantedBookToleratesADanglingEntityLink() {
        await using var db = CreateContext();
        AddHintWithEntity(db, Guid.NewGuid(), "/media/books/Author/Title/Title.epub");
        await db.SaveChangesAsync();

        Assert.False(await new AcquisitionHintApplier(db).BindWantedEntityAsync(EntityKind.Book,
            "/media/books/Author/Title/Title.epub", CancellationToken.None));
    }

    [Fact]
    public async Task BindWantedBookRetriesWhenAnExistingEntityIsLifecycleClaimed() {
        await using var db = CreateContext();
        var entityId = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Elantris");
        AddHintWithEntity(db, entityId, "/media/books/Author/Title/Title.epub");
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<EntityLifecycleMutationConflictException>(() =>
            new AcquisitionHintApplier(db, lifecycle: new RejectingLifecycleLease())
                .BindWantedEntityAsync(
                    EntityKind.Book,
                    "/media/books/Author/Title/Title.epub",
                    CancellationToken.None));

        Assert.Equal(entityId, exception.EntityId);
        Assert.Empty(await db.EntityFiles.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task BindWantedBookNeverRebindsAnEntityThatAlreadyHasASource() {
        await using var db = CreateContext();
        var entityId = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Elantris");
        var now = DateTimeOffset.UtcNow;
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = entityId, Role = EntityFileRole.Source,
            Path = "/media/books/existing.epub", CreatedAt = now, UpdatedAt = now
        });
        AddHintWithEntity(db, entityId, "/media/books/Author/Title/Title.epub");
        await db.SaveChangesAsync();

        Assert.False(await new AcquisitionHintApplier(db).BindWantedEntityAsync(EntityKind.Book,
            "/media/books/Author/Title/Title.epub", CancellationToken.None));
        Assert.Single(await db.EntityFiles.AsNoTracking().Where(f => f.EntityId == entityId).ToArrayAsync());
    }

    [Fact]
    public async Task BindWantedAuthorAttachesTheFolderToTheWantedBooksParent() {
        await using var db = CreateContext();
        var authorId = AddWantedEntity(db, EntityKindRegistry.BookAuthor.Code, "Brandon Sanderson");
        var bookId = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Elantris", parentEntityId: authorId);
        AddHintWithEntity(db, bookId, "/media/books/Brandon Sanderson/Elantris (2005)/Elantris.epub");
        await db.SaveChangesAsync();

        var bound = await new AcquisitionHintApplier(db).BindWantedParentAsync(EntityKind.BookAuthor,
            "/media/books/Brandon Sanderson", CancellationToken.None);

        Assert.True(bound);
        var author = await db.Entities.AsNoTracking().FirstAsync(row => row.Id == authorId);
        Assert.False(author.IsWanted);
        var file = Assert.Single(await db.EntityFiles.AsNoTracking().Where(f => f.EntityId == authorId).ToArrayAsync());
        Assert.Equal("/media/books/Brandon Sanderson", file.Path);
        // The book itself stays wanted until its own path binds.
        Assert.True((await db.Entities.AsNoTracking().FirstAsync(row => row.Id == bookId)).IsWanted);
    }

    [Fact]
    public async Task BindWantedParentWalksAnArbitrarilyDeepEntityHierarchy() {
        await using var db = CreateContext();
        var authorId = AddWantedEntity(db, EntityKindRegistry.BookAuthor.Code, "Author");
        var levelOne = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Level 1", authorId);
        var levelTwo = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Level 2", levelOne);
        var levelThree = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Level 3", levelTwo);
        var levelFour = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Level 4", levelThree);
        var leafId = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Leaf", levelFour);
        AddHintWithEntity(db, leafId, "/media/books/Author/Leaf.epub");
        await db.SaveChangesAsync();

        var bound = await new AcquisitionHintApplier(db).BindWantedParentAsync(
            EntityKind.BookAuthor,
            "/media/books/Author",
            CancellationToken.None);

        Assert.True(bound);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == authorId)).IsWanted);
        Assert.Equal(
            "/media/books/Author",
            (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.EntityId == authorId)).Path);
    }

    [Fact]
    public async Task BindIgnoresHintsWithNoWantedEntityLink() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = Guid.NewGuid(), EntityId = null,
            SourcePath = "/media/books/Author/Title/Title.epub", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            Consumed = false, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        Assert.False(await new AcquisitionHintApplier(db).BindWantedEntityAsync(EntityKind.Book,
            "/media/books/Author/Title/Title.epub", CancellationToken.None));
    }

    private static Guid AddWantedEntity(PrismediaDbContext db, string kindCode, string title, Guid? parentEntityId = null) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kindCode, Title = title, ParentEntityId = parentEntityId,
            IsWanted = true, CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    [Fact]
    public async Task GetImportContextCarriesTheLinkedEntity() {
        // The import engines redirect into an existing entity's folder — the link must survive the trip.
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Downloaded, Title = "Show", EntityId = entityId,
            Kind = EntityKind.VideoSeason, SeasonNumber = 2,
            FinalSourcePath = "/media/Show/Season 02",
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var context = await AcquisitionTestFactory.Store(db).GetImportContextAsync(acquisitionId, CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal(entityId, context!.EntityId);
        Assert.Equal(2, context.SeasonNumber);
        Assert.Equal("/media/Show/Season 02", context.FinalSourcePath);
    }

    [Fact]
    public async Task AcquisitionIdentityPreservesCaseSensitiveColonValueThroughTheImportHint() {
        await using var db = CreateContext();
        var store = AcquisitionTestFactory.Store(db);
        var identity = new ExternalIdentity(" TMDB ", " Series:Episode:AbC ");
        var acquisition = await store.CreateAsync(
            new AcquisitionMetadata("Show", null, null, null, null, identity),
            CancellationToken.None);

        var context = await store.GetImportContextAsync(acquisition.Id, CancellationToken.None);

        Assert.Equal(identity, context!.ExternalIdentity);
        await store.WriteImportHintAsync(
            acquisition.Id,
            "/media/tv/Show/S01/Episode.mkv",
            context,
            BookQualityRank.Floor,
            CancellationToken.None);
        var hint = await db.AcquisitionImportHints.AsNoTracking().SingleAsync();
        Assert.Equal("tmdb", hint.IdentityNamespace);
        Assert.Equal("Series:Episode:AbC", hint.IdentityValue);
    }

    [Fact]
    public async Task CorruptTvCheckpointFailsClosedInsteadOfFallingBackToTheBroadFinalPath() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Importing,
            Title = "Show",
            Kind = EntityKind.VideoSeason,
            FinalSourcePath = "/media/tv/Show",
            ImportCheckpointJson = "{not-valid-json",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            AcquisitionTestFactory.Store(db).GetImportContextAsync(acquisitionId, CancellationToken.None));

        Assert.Contains("cannot be resumed safely", exception.Message);
    }

    [Fact]
    public async Task StrictTvCheckpointRecoveryShapeRoundTripsThroughTheStore() {
        await using var db = CreateContext();
        var acquisitionId = AddCheckpointAcquisition(db);
        await db.SaveChangesAsync();
        var checkpoint = ValidTvCheckpoint();
        var store = AcquisitionTestFactory.Store(db);

        await store.SetTvImportCheckpointAsync(acquisitionId, checkpoint, CancellationToken.None);
        var context = await store.GetImportContextAsync(acquisitionId, CancellationToken.None);

        var decoded = Assert.IsType<TvImportCheckpoint>(context?.TvImportCheckpoint);
        Assert.Equal(checkpoint.LibraryRootId, decoded.LibraryRootId);
        Assert.Equal(checkpoint.AttemptId, decoded.AttemptId);
        Assert.Equal(checkpoint.ClaimJobId, decoded.ClaimJobId);
        var unit = Assert.Single(decoded.Units);
        Assert.Equal(checkpoint.Units[0].TargetAbsolutePath, unit.TargetAbsolutePath);
        Assert.Equal(checkpoint.Units[0].ReplacementBackupPath, unit.ReplacementBackupPath);
        Assert.Equal(checkpoint.Units[0].ReplacementEvidencePath, unit.ReplacementEvidencePath);
    }

    [Fact]
    public async Task StatusTransitionClaimsOnlyTheExpectedLifecycleState() {
        await using var db = CreateContext();
        var acquisitionId = AddCheckpointAcquisition(db);
        var row = db.Acquisitions.Local.Single(value => value.Id == acquisitionId);
        row.Status = AcquisitionStatus.Downloaded;
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        Assert.False(await store.TryTransitionStatusAsync(
            acquisitionId,
            [AcquisitionStatus.AwaitingSelection],
            AcquisitionStatus.Importing,
            null,
            CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Downloaded, await store.GetStatusAsync(acquisitionId, CancellationToken.None));

        Assert.True(await store.TryTransitionStatusAsync(
            acquisitionId,
            [AcquisitionStatus.Downloaded],
            AcquisitionStatus.Importing,
            null,
            CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Importing, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Fact]
    public async Task InitialCheckpointCreationRequiresImportingAndTheCurrentTransfer() {
        await using var db = CreateContext();
        var acquisitionId = AddCheckpointAcquisition(db);
        db.Acquisitions.Local.Single(row => row.Id == acquisitionId).Status = AcquisitionStatus.Downloaded;
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "transfer-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var checkpoint = ValidTvCheckpoint();

        Assert.True(await store.TryClaimInitialImportAsync(
            acquisitionId,
            checkpoint.ClaimJobId,
            allowManualRetry: false,
            CancellationToken.None));
        Assert.True(await store.TryCreateTvImportCheckpointAsync(acquisitionId, checkpoint, CancellationToken.None));
        Assert.False(await store.TryCreateTvImportCheckpointAsync(
            acquisitionId,
            checkpoint with { AttemptId = Guid.NewGuid() },
            CancellationToken.None));
        Assert.Equal(
            checkpoint.AttemptId,
            (await store.GetImportContextAsync(acquisitionId, CancellationToken.None))!.TvImportCheckpoint!.AttemptId);
    }

    [Fact]
    public async Task ImportClaimLetsOnlyTheOwningJobRecoverImportingOrFailedWork() {
        await using var db = CreateContext();
        var acquisitionId = AddCheckpointAcquisition(db);
        db.Acquisitions.Local.Single(row => row.Id == acquisitionId).Status = AcquisitionStatus.Downloaded;
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var ownerJobId = Guid.NewGuid();

        Assert.True(await store.TryClaimInitialImportAsync(
            acquisitionId, ownerJobId, allowManualRetry: false, CancellationToken.None));
        Assert.True(await store.TryClaimInitialImportAsync(
            acquisitionId, ownerJobId, allowManualRetry: false, CancellationToken.None));
        Assert.False(await store.TryClaimInitialImportAsync(
            acquisitionId, Guid.NewGuid(), allowManualRetry: false, CancellationToken.None));

        await store.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, "retry", CancellationToken.None);
        Assert.True(await store.TryClaimInitialImportAsync(
            acquisitionId, ownerJobId, allowManualRetry: false, CancellationToken.None));
        Assert.False(await store.TryClaimInitialImportAsync(
            acquisitionId, Guid.NewGuid(), allowManualRetry: false, CancellationToken.None));
    }

    [Fact]
    public async Task ExplicitManualRetryMayClaimAHeldImportWithANewJob() {
        await using var db = CreateContext();
        var acquisitionId = AddCheckpointAcquisition(db);
        db.Acquisitions.Local.Single(row => row.Id == acquisitionId).Status = AcquisitionStatus.ManualImportRequired;
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        Assert.False(await store.TryClaimInitialImportAsync(
            acquisitionId, Guid.NewGuid(), allowManualRetry: false, CancellationToken.None));
        Assert.True(await store.TryClaimInitialImportAsync(
            acquisitionId, Guid.NewGuid(), allowManualRetry: true, CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Importing, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Fact]
    public async Task CheckpointClaimIsExclusiveWhileImportingButMayMoveToANewRetryJobAfterFailure() {
        await using var db = CreateContext();
        var acquisitionId = AddCheckpointAcquisition(db);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var checkpoint = ValidTvCheckpoint();
        await store.SetTvImportCheckpointAsync(acquisitionId, checkpoint, CancellationToken.None);

        Assert.False(await store.TryClaimTvImportCheckpointAsync(
            acquisitionId,
            checkpoint,
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.True(await store.TryClaimTvImportCheckpointAsync(
            acquisitionId,
            checkpoint,
            checkpoint.ClaimJobId,
            CancellationToken.None));

        await store.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, "retry", CancellationToken.None);
        var retryJobId = Guid.NewGuid();
        Assert.True(await store.TryClaimTvImportCheckpointAsync(
            acquisitionId,
            checkpoint,
            retryJobId,
            CancellationToken.None));
        Assert.Equal(
            retryJobId,
            (await store.GetImportContextAsync(acquisitionId, CancellationToken.None))!.TvImportCheckpoint!.ClaimJobId);
    }

    [Theory]
    [InlineData("unknown-import-mode")]
    [InlineData("empty-library-id")]
    [InlineData("empty-attempt-id")]
    [InlineData("empty-claim-job-id")]
    [InlineData("blank-series-path")]
    [InlineData("relative-series-path")]
    [InlineData("blank-transfer-id")]
    [InlineData("empty-units")]
    [InlineData("null-units")]
    [InlineData("null-unit")]
    [InlineData("blank-source-relative-path")]
    [InlineData("absolute-source-relative-path")]
    [InlineData("blank-target-path")]
    [InlineData("relative-target-path")]
    [InlineData("blank-source-absolute-path")]
    [InlineData("relative-source-absolute-path")]
    [InlineData("zero-season")]
    [InlineData("zero-episode")]
    [InlineData("null-covered-episodes")]
    [InlineData("invalid-covered-episode")]
    [InlineData("missing-previous-path")]
    [InlineData("relative-previous-path")]
    [InlineData("previous-outside-series")]
    [InlineData("missing-backup-path")]
    [InlineData("wrong-backup-path")]
    [InlineData("missing-evidence-path")]
    [InlineData("wrong-evidence-path")]
    [InlineData("wrong-replacement-target")]
    [InlineData("format-change-without-consent")]
    [InlineData("mismatched-final-path")]
    public async Task InvalidTvCheckpointRecoveryShapeFailsClosed(string invalidCase) {
        await using var db = CreateContext();
        var acquisitionId = AddCheckpointAcquisition(db);
        await db.SaveChangesAsync();
        var row = await db.Acquisitions.SingleAsync(value => value.Id == acquisitionId);
        row.ImportCheckpointJson = InvalidTvCheckpointJson(invalidCase);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            AcquisitionTestFactory.Store(db).GetImportContextAsync(acquisitionId, CancellationToken.None));

        Assert.Contains("cannot be resumed safely", exception.Message);
    }

    private static Guid AddCheckpointAcquisition(PrismediaDbContext db) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id,
            Status = AcquisitionStatus.Importing,
            Title = "Show",
            Kind = EntityKind.VideoSeason,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        return id;
    }

    private static TvImportCheckpoint ValidTvCheckpoint() {
        var attemptId = Guid.NewGuid();
        var seriesFolder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "prismedia-checkpoint", "Show"));
        var previousPath = Path.Combine(seriesFolder, "Season 01", "Show - S01E01.mkv");
        var sourcePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "prismedia-download", "Show.S01E01.mkv"));
        return new TvImportCheckpoint(
            Guid.NewGuid(),
            seriesFolder,
            ImportMode.Move,
            AllowFormatChange: false,
            SuccessMessage: "Imported into the existing series.",
            PreferSingleFileFinalSource: true,
            Units: [new TvImportCheckpointUnit(
                "Show.S01E01.mkv",
                previousPath,
                1,
                1,
                [],
                PreviousFilePath: previousPath,
                SourceAbsolutePath: sourcePath,
                ReplacementBackupPath: OwnedFileReplacementArtifacts.CheckpointBackupPath(previousPath, attemptId),
                ReplacementEvidencePath: OwnedFileReplacementArtifacts.CheckpointEvidencePath(previousPath, attemptId))],
            TransferClientItemId: "transfer-1",
            AttemptId: attemptId,
            ClaimJobId: Guid.NewGuid());
    }

    private static string InvalidTvCheckpointJson(string invalidCase) {
        var checkpoint = ValidTvCheckpoint();
        var root = JsonNode.Parse(TvImportCheckpointJson.Serialize(checkpoint))!.AsObject();
        var unit = root["Units"]!.AsArray()[0]!.AsObject();
        var otherPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "prismedia-checkpoint", "other.mkv"));
        var otherSeriesPath = Path.Combine(checkpoint.SeriesFolderPath, "Season 01", "other.mkv");

        switch (invalidCase) {
            case "unknown-import-mode": root["ImportMode"] = "unknown-mode"; break;
            case "empty-library-id": root["LibraryRootId"] = Guid.Empty; break;
            case "empty-attempt-id": root["AttemptId"] = Guid.Empty; break;
            case "empty-claim-job-id": root["ClaimJobId"] = Guid.Empty; break;
            case "blank-series-path": root["SeriesFolderPath"] = " "; break;
            case "relative-series-path": root["SeriesFolderPath"] = "Show"; break;
            case "blank-transfer-id": root["TransferClientItemId"] = " "; break;
            case "empty-units": root["Units"] = new JsonArray(); break;
            case "null-units": root["Units"] = null; break;
            case "null-unit": root["Units"] = new JsonArray((JsonNode?)null); break;
            case "blank-source-relative-path": unit["SourceRelativePath"] = " "; break;
            case "absolute-source-relative-path": unit["SourceRelativePath"] = otherPath; break;
            case "blank-target-path": unit["TargetAbsolutePath"] = " "; break;
            case "relative-target-path": unit["TargetAbsolutePath"] = "Show.S01E01.mkv"; break;
            case "blank-source-absolute-path": unit["SourceAbsolutePath"] = " "; break;
            case "relative-source-absolute-path": unit["SourceAbsolutePath"] = "Show.S01E01.mkv"; break;
            case "zero-season": unit["SeasonNumber"] = 0; break;
            case "zero-episode": unit["EpisodeNumber"] = 0; break;
            case "null-covered-episodes": unit["CoveredEpisodeNumbers"] = null; break;
            case "invalid-covered-episode": unit["CoveredEpisodeNumbers"] = new JsonArray(0); break;
            case "missing-previous-path": unit["PreviousFilePath"] = null; break;
            case "relative-previous-path": unit["PreviousFilePath"] = "Show - S01E01.mkv"; break;
            case "previous-outside-series": unit["PreviousFilePath"] = otherPath; break;
            case "missing-backup-path": unit["ReplacementBackupPath"] = null; break;
            case "wrong-backup-path": unit["ReplacementBackupPath"] = otherPath; break;
            case "missing-evidence-path": unit["ReplacementEvidencePath"] = null; break;
            case "wrong-evidence-path": unit["ReplacementEvidencePath"] = otherPath; break;
            case "wrong-replacement-target": unit["TargetAbsolutePath"] = otherSeriesPath; break;
            case "format-change-without-consent":
                unit["SourceRelativePath"] = "Show.S01E01.mp4";
                unit["SourceAbsolutePath"] = Path.ChangeExtension(unit["SourceAbsolutePath"]!.GetValue<string>(), ".mp4");
                unit["TargetAbsolutePath"] = Path.ChangeExtension(unit["PreviousFilePath"]!.GetValue<string>(), ".mp4");
                break;
            case "mismatched-final-path": unit["FinalPath"] = otherSeriesPath; break;
            default: throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null);
        }

        return root.ToJsonString();
    }

    private static void AddHintWithEntity(PrismediaDbContext db, Guid entityId, string sourcePath) {
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "B", EntityId = entityId,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, EntityId = entityId, SourcePath = sourcePath,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", Consumed = false, CreatedAt = now, UpdatedAt = now
        });
    }

    private static void AddCandidate(PrismediaDbContext db, Guid acquisitionId, string? infoHash, string indexer, string title, double score) {
        var now = DateTimeOffset.UtcNow;
        if (db.Acquisitions.Local.All(a => a.Id != acquisitionId) && !db.Acquisitions.Any(a => a.Id == acquisitionId)) {
            db.Acquisitions.Add(new AcquisitionRow { Id = acquisitionId, Status = AcquisitionStatus.AwaitingSelection, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        }

        db.ReleaseCandidates.Add(new ReleaseCandidateRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, IndexerName = indexer, Title = title,
            InfoHash = infoHash, Accepted = true, Score = score, Protocol = DownloadProtocol.Torrent, RejectionsJson = "[]", CreatedAt = now
        });
    }

    private static ScoredRelease Scored(string title, string infoHash) =>
        new(
            new IndexerRelease(
                title,
                1_000,
                10,
                2,
                DownloadProtocol.Torrent,
                "https://indexer.test/download",
                null,
                infoHash,
                null,
                null,
                DateTimeOffset.UtcNow),
            Guid.NewGuid(),
            "Indexer",
            Accepted: true,
            Score: 100,
            Rejections: []);

    private sealed class RejectingLifecycleLease : IEntityLifecycleMutationLease {
        public Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
