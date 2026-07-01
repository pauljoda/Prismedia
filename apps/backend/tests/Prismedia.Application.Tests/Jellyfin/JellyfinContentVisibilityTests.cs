using Prismedia.Application.Jellyfin;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Jellyfin;

/// <summary>
/// Covers the Jellyfin visibility rule: content-class (SFW/NSFW) gating, and the locked decision that
/// wanted placeholders — request-created entities with no file yet — never reach Jellyfin clients.
/// </summary>
public sealed class JellyfinContentVisibilityTests {
    [Fact]
    public void WantedThumbnailsAreNeverVisible() {
        var visibility = new JellyfinContentVisibility(AllowSfw: true, AllowNsfw: true);

        Assert.True(visibility.Allows(Thumbnail(isWanted: false)));
        Assert.False(visibility.Allows(Thumbnail(isWanted: true)));
    }

    [Fact]
    public void WantedCardsAreNeverVisible() {
        var visibility = new JellyfinContentVisibility(AllowSfw: true, AllowNsfw: true);

        Assert.True(visibility.Allows(Card(isWanted: false)));
        Assert.False(visibility.Allows(Card(isWanted: true)));
    }

    [Fact]
    public void NsfwGatingStillAppliesToNonWantedItems() {
        var sfwOnly = JellyfinContentVisibility.SfwOnly;

        Assert.True(sfwOnly.Allows(Thumbnail(isWanted: false, isNsfw: false)));
        Assert.False(sfwOnly.Allows(Thumbnail(isWanted: false, isNsfw: true)));
    }

    private static EntityThumbnail Thumbnail(bool isWanted, bool isNsfw = false) =>
        new(Guid.NewGuid(), EntityKind.Book, "Book", null, null, null, null,
            ThumbnailHoverKind.None, null, [], [], null, false, isNsfw, false) { IsWanted = isWanted };

    private static EntityCard Card(bool isWanted) =>
        new() {
            Id = Guid.NewGuid(),
            Kind = EntityKind.Book,
            Title = "Book",
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [new FlagsCapability(false, false, false, isWanted)],
            ChildrenByKind = [],
            Relationships = []
        };
}
