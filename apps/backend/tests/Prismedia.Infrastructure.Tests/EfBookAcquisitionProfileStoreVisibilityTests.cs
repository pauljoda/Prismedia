using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Acquisition profiles inherit visibility from the library root they import into.</summary>
public sealed class EfBookAcquisitionProfileStoreVisibilityTests {
    [Fact]
    public async Task MissingProfileRetainsTheRequestedAcquisitionKind() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase($"profile-default-kind-{Guid.NewGuid():N}")
                .Options);

        var rules = await new EfBookAcquisitionProfileStore(db)
            .GetRulesAsync(profileId: null, kind: EntityKind.Movie, cancellationToken: CancellationToken.None);

        Assert.Equal(EntityKind.Movie, rules.Kind);
    }

    [Fact]
    public async Task SfwModeOmitsProfilesThatTargetNsfwLibraries() {
        await using var db = await SeedAsync();
        var store = new EfBookAcquisitionProfileStore(db);

        var profiles = await store.ListAsync(hideNsfw: true, allowedRootIds: null, CancellationToken.None);

        Assert.Equal(["SFW profile"], profiles.Select(profile => profile.DisplayName).ToArray());
    }

    [Fact]
    public async Task ProfileListIncludesOnlyLibrariesGrantedToTheCurrentUser() {
        await using var db = await SeedAsync();
        var store = new EfBookAcquisitionProfileStore(db);

        var profiles = await store.ListAsync(
            hideNsfw: false,
            allowedRootIds: new HashSet<Guid> { SfwRootId },
            CancellationToken.None);

        Assert.Equal(["SFW profile"], profiles.Select(profile => profile.DisplayName).ToArray());
    }

    private static readonly Guid SfwRootId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NsfwRootId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static async Task<PrismediaDbContext> SeedAsync() {
        var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase($"profile-visibility-{Guid.NewGuid():N}")
                .Options);
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            Root(SfwRootId, "SFW", isNsfw: false, now),
            Root(NsfwRootId, "NSFW", isNsfw: true, now));
        db.BookAcquisitionProfiles.AddRange(
            Profile("SFW profile", SfwRootId, now),
            Profile("NSFW profile", NsfwRootId, now));
        await db.SaveChangesAsync();
        return db;
    }

    private static LibraryRootRow Root(Guid id, string label, bool isNsfw, DateTimeOffset now) => new() {
        Id = id,
        Path = $"/media/{label.ToLowerInvariant()}",
        Label = label,
        Enabled = true,
        ScanBooks = true,
        IsNsfw = isNsfw,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static BookAcquisitionProfileRow Profile(string name, Guid targetRootId, DateTimeOffset now) => new() {
        Id = Guid.NewGuid(),
        Kind = EntityKind.Book,
        DisplayName = name,
        TargetLibraryRootId = targetRootId,
        PathTemplate = MediaNamingTemplates.BookDefault,
        CreatedAt = now,
        UpdatedAt = now
    };
}
