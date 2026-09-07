using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class AcquisitionCandidateValidatorTests {
    [Theory]
    [InlineData("Happy", false)]
    [InlineData("Happy (Instrumental)", true)]
    public async Task StoredTrackCandidateRechecksItsAdvertisedRecording(string requestedTitle, bool accepted) {
        await using var db = new PrismediaDbContext(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var row = new AcquisitionRow { Id = Guid.NewGuid(), Kind = EntityKind.AudioTrack,
            Title = requestedTitle, Author = "Pharrell Williams", Status = AcquisitionStatus.Searching };
        db.Acquisitions.Add(row);
        db.DownloadClientConfigs.Add(new DownloadClientConfigRow { Id = Guid.NewGuid(), Kind = DownloadClientKind.Slskd, Enabled = true });
        await db.SaveChangesAsync();
        var locator = SoulseekLocator.Encode(new(Guid.NewGuid(), "peer",
            [new("Music\\Pharrell Williams\\02-pharrell_williams-happy_(instrumental).flac", 30_000_000)]));
        var release = new IndexerRelease("Pharrell Williams / Happy / Happy (Instrumental).flac FLAC", 30_000_000,
            null, null, DownloadProtocol.Soulseek, locator, null, null, null, null, null);
        var store = AcquisitionTestFactory.Store(db);
        Assert.True(await store.TryCompleteSearchAsync(row.Id, [new(release, null, "Soulseek", true, 100, [])], null, default));
        var candidate = (await store.GetQueueCandidateAsync(row.Id, (await db.ReleaseCandidates.SingleAsync()).Id, default))!;
        var validator = new AcquisitionCandidateValidator(store, new EfBookAcquisitionProfileStore(db),
            new EfDownloadClientConfigStore(db), new AcquisitionPolicyRegistry([new BookAcquisitionPolicyModule(), new MovieAcquisitionPolicyModule(),
                new MusicAcquisitionPolicyModule(), new TvAcquisitionPolicyModule()]),
            new SettingsService(new EfSettingsPersistence(db)), new SoulseekReleaseInventory());
        var rejections = await validator.ValidateAsync(row.Id, candidate, default);
        if (accepted) Assert.Empty(rejections);
        else Assert.Contains(ReleaseRejectionReason.TitleMismatch, rejections);
    }

    [Fact]
    public async Task StoredCandidateKeepsIndexerFactsAndUsesCurrentProfileAndRequestMetadata() {
        await using var db = new PrismediaDbContext(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var profile = new BookAcquisitionProfileRow { Id = Guid.NewGuid(), Kind = EntityKind.Movie,
            AllowedQualities = [VideoQuality.Webdl720p.ToCode()], PreferredLanguages = ["en"] };
        var row = new AcquisitionRow { Id = Guid.NewGuid(), Kind = EntityKind.Movie, Title = "Film", Year = 2020,
            Status = AcquisitionStatus.Searching, ProfileId = profile.Id };
        db.BookAcquisitionProfiles.Add(profile); db.Acquisitions.Add(row);
        db.DownloadClientConfigs.Add(new DownloadClientConfigRow { Id = Guid.NewGuid(), Kind = DownloadClientKind.QBittorrent, Enabled = true });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var release = new IndexerRelease("Film 2020 720p WEB-DL", 5_000_000_000, 20, 2, DownloadProtocol.Torrent,
            "https://indexer.test/download", null, "test-hash", null, "en", DateTimeOffset.UtcNow);
        Assert.True(await store.TryCompleteSearchAsync(row.Id, [new(release, null, "Indexer", true, 100, [])], null, CancellationToken.None));
        var candidate = (await store.GetQueueCandidateAsync(row.Id, (await db.ReleaseCandidates.SingleAsync()).Id, CancellationToken.None))!;
        Assert.Equal(release.Language, candidate.Language);
        Assert.Equal(release.SizeBytes, candidate.SizeBytes);
        Assert.Equal(release.Seeders, candidate.Seeders);
        var validator = new AcquisitionCandidateValidator(store, new EfBookAcquisitionProfileStore(db),
            new EfDownloadClientConfigStore(db), new AcquisitionPolicyRegistry([
                new BookAcquisitionPolicyModule(), new MovieAcquisitionPolicyModule(), new MusicAcquisitionPolicyModule(), new TvAcquisitionPolicyModule()]),
            new SettingsService(new EfSettingsPersistence(db)));
        Assert.Empty(await validator.ValidateAsync(row.Id, candidate, CancellationToken.None));

        profile.AllowedQualities = [VideoQuality.Webdl1080p.ToCode()];
        await db.SaveChangesAsync();
        Assert.Contains(ReleaseRejectionReason.QualityNotAllowed, await validator.ValidateAsync(row.Id, candidate, CancellationToken.None));
        profile.AllowedQualities = [VideoQuality.Webdl720p.ToCode()];
        profile.MaxSizeBytes = 4_000_000_000;
        await db.SaveChangesAsync();
        Assert.Contains(ReleaseRejectionReason.SizeOutOfRange, await validator.ValidateAsync(row.Id, candidate, CancellationToken.None));
        profile.MaxSizeBytes = null;
        profile.PreferredLanguages = ["fr"];
        await db.SaveChangesAsync();
        Assert.Contains(ReleaseRejectionReason.LanguageMismatch, await validator.ValidateAsync(row.Id, candidate, CancellationToken.None));
        profile.PreferredLanguages = ["en"];
        row.Year = 2025;
        await db.SaveChangesAsync();
        Assert.Contains(ReleaseRejectionReason.WrongYear, await validator.ValidateAsync(row.Id, candidate, CancellationToken.None));
    }
}
