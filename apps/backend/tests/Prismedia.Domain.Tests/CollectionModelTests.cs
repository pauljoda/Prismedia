using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class CollectionModelTests {
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void SharingKeepsOwnershipAndExpandsReadScope() {
        var otherUserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var collection = new Collection(
            Guid.Parse("99999999-9999-4999-8999-999999999999"),
            "Household Picks",
            OwnerUserId);

        Assert.True(collection.IsOwnedBy(OwnerUserId));
        Assert.True(collection.CanView(OwnerUserId));
        Assert.True(collection.CanContributeItems(OwnerUserId));
        Assert.False(collection.CanView(otherUserId));
        Assert.False(collection.CanContributeItems(otherUserId));

        collection.SetSharing(true);

        Assert.Equal(OwnerUserId, collection.OwnerUserId);
        Assert.True(collection.IsShared);
        Assert.True(collection.CanView(otherUserId));
        Assert.True(collection.CanContributeItems(otherUserId));
        Assert.False(collection.IsOwnedBy(otherUserId));
    }

    [Fact]
    public void CollectionRequiresAnOwner() {
        Assert.Throws<ArgumentException>(() =>
            new Collection(Guid.NewGuid(), "Ownerless", Guid.Empty));
    }

    [Fact]
    public void ConfigureRulesNormalizesManualCollectionsAndKeepsCoverSettings() {
        var collection = new Collection(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Watch Later",
            OwnerUserId,
            CollectionMode.Dynamic,
            "{\"type\":\"group\"}",
            CollectionCoverMode.Mosaic);

        collection.ConfigureRules(CollectionMode.Manual, "{\"ignored\":true}");
        collection.SetCover(CollectionCoverMode.Item, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        Assert.Equal(CollectionMode.Manual, collection.Mode);
        Assert.Null(collection.RuleTreeJson);
        Assert.Equal(CollectionCoverMode.Item, collection.CoverMode);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), collection.CoverItemId);
    }

    [Fact]
    public void ConfigureRulesRequiresRulesForRuleDrivenCollections() {
        var collection = new Collection(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "Smart Picks",
            OwnerUserId);

        var ex = Assert.Throws<ArgumentException>(() =>
            collection.ConfigureRules(CollectionMode.Dynamic, null));

        Assert.Contains("Rule-driven collections require", ex.Message);
    }

    [Fact]
    public void ManualMembershipIsDomainGuardedByModeAndItemKind() {
        var manual = new Collection(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "Manual",
            OwnerUserId);
        var dynamic = new Collection(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "Dynamic",
            OwnerUserId,
            CollectionMode.Dynamic,
            "{\"type\":\"group\"}");

        Assert.True(manual.CanEditManualMembership);
        Assert.False(dynamic.CanEditManualMembership);
        Assert.True(Collection.CanContain(EntityKind.VideoSeries));
        Assert.True(Collection.CanContain(EntityKind.Movie));
        Assert.True(Collection.CanContain(EntityKind.MusicArtist));
        Assert.True(Collection.CanContain(EntityKind.AudioLibrary));
        Assert.True(Collection.CanContain(EntityKind.AudioTrack));
        Assert.False(Collection.CanContain(EntityKind.Collection));
    }
}
