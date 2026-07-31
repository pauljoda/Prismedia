using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Security;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// The NSFW wall on library grants: a user whose account blocks NSFW content can never hold a grant
/// to an NSFW library — every write path filters silently, and blocking a user's NSFW permission
/// revokes any grants they already had.
/// </summary>
public sealed class LibraryAccessNsfwWallTests {
    private static readonly Guid SfwRootId = Guid.NewGuid();
    private static readonly Guid NsfwRootId = Guid.NewGuid();
    private static readonly Guid BlockedUserId = Guid.NewGuid();
    private static readonly Guid AllowedUserId = Guid.NewGuid();

    [Fact]
    public async Task ReplaceUserAccessDropsNsfwRootsForABlockedUser() {
        await using var db = await SeedAsync();
        var store = new EfLibraryAccessReader(db);

        await store.ReplaceUserAccessAsync(BlockedUserId, [SfwRootId, NsfwRootId], CancellationToken.None);

        Assert.Equal([SfwRootId], (await store.GetAllowedRootIdsAsync(BlockedUserId, CancellationToken.None)).ToArray());
    }

    [Fact]
    public async Task ReplaceUserAccessKeepsNsfwRootsForAnAllowedUser() {
        await using var db = await SeedAsync();
        var store = new EfLibraryAccessReader(db);

        await store.ReplaceUserAccessAsync(AllowedUserId, [SfwRootId, NsfwRootId], CancellationToken.None);

        Assert.Equal(2, (await store.GetAllowedRootIdsAsync(AllowedUserId, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task RootGrantsExcludeBlockedUsersForAnNsfwRoot() {
        await using var db = await SeedAsync();
        var store = new EfLibraryAccessReader(db);

        await store.GrantRootAccessAsync(NsfwRootId, [BlockedUserId, AllowedUserId], CancellationToken.None);
        await store.ReplaceRootAccessAsync(NsfwRootId, [BlockedUserId, AllowedUserId], CancellationToken.None);

        var byRoot = await store.GetAccessByRootAsync(CancellationToken.None);
        Assert.Equal([AllowedUserId], byRoot[NsfwRootId].ToArray());
    }

    [Fact]
    public async Task RevokeNsfwAccessRemovesOnlyNsfwGrants() {
        await using var db = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        db.UserLibraryAccess.AddRange(
            new UserLibraryAccessRow { UserId = BlockedUserId, LibraryRootId = SfwRootId, CreatedAt = now },
            new UserLibraryAccessRow { UserId = BlockedUserId, LibraryRootId = NsfwRootId, CreatedAt = now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); // the store re-loads tracked instances of these rows
        var store = new EfLibraryAccessReader(db);

        await store.RevokeNsfwAccessAsync(BlockedUserId, CancellationToken.None);

        Assert.Equal([SfwRootId], (await store.GetAllowedRootIdsAsync(BlockedUserId, CancellationToken.None)).ToArray());
    }

    private static async Task<PrismediaDbContext> SeedAsync() {
        var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            new LibraryRootRow { Id = SfwRootId, Path = "/media/sfw", Label = "SFW", Enabled = true, CreatedAt = now, UpdatedAt = now },
            new LibraryRootRow { Id = NsfwRootId, Path = "/media/nsfw", Label = "NSFW", Enabled = true, IsNsfw = true, CreatedAt = now, UpdatedAt = now });
        db.Users.AddRange(
            User(BlockedUserId, "blocked", allowNsfw: false),
            User(AllowedUserId, "allowed", allowNsfw: true));
        await db.SaveChangesAsync();
        return db;
    }

    private static UserRow User(Guid id, string username, bool allowNsfw) {
        var now = DateTimeOffset.UtcNow;
        return new UserRow {
            Id = id, Username = username, NormalizedUsername = username, DisplayName = username,
            Role = UserRole.Member, AllowNsfw = allowNsfw, Enabled = true,
            CreatedAt = now, UpdatedAt = now
        };
    }
}
