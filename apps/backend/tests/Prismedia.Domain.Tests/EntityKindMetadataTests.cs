using System.Reflection;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class EntityKindMetadataTests {
    [Fact]
    public void EveryEntityKindDeclaresCodeAndMetaInline() {
        foreach (var field in typeof(EntityKind).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            Assert.True(
                field.GetCustomAttribute<CodeAttribute>() is not null,
                $"EntityKind.{field.Name} is missing a [Code] attribute.");
            Assert.True(
                field.GetCustomAttribute<EntityKindMetaAttribute>() is not null,
                $"EntityKind.{field.Name} is missing an [EntityKindMeta] attribute.");
        }
    }

    [Theory]
    [InlineData("video-series", true)]
    [InlineData("video-season", true)]
    [InlineData("audio-library", true)]
    [InlineData("music-artist", true)]
    [InlineData("book", true)]
    [InlineData("book-volume", true)]
    [InlineData("movie", false)]
    [InlineData("video", false)]
    [InlineData("image", false)]
    [InlineData("audio-track", false)]
    [InlineData("gallery", false)]
    public void EnumeratesIdentifyChildrenMatchesContainerClassification(string code, bool expected) {
        Assert.Equal(expected, EntityKindRegistry.EnumeratesIdentifyChildren(code));
    }

    [Theory]
    [InlineData(EntityKind.Audio, true)]
    [InlineData(EntityKind.AudioLibrary, true)]
    [InlineData(EntityKind.AudioTrack, true)]
    [InlineData(EntityKind.Book, true)]
    [InlineData(EntityKind.BookAuthor, true)]
    [InlineData(EntityKind.BookChapter, false)]
    [InlineData(EntityKind.BookVolume, true)]
    [InlineData(EntityKind.Gallery, true)]
    [InlineData(EntityKind.Image, true)]
    [InlineData(EntityKind.Movie, true)]
    [InlineData(EntityKind.MusicArtist, true)]
    [InlineData(EntityKind.Video, true)]
    [InlineData(EntityKind.VideoSeason, true)]
    [InlineData(EntityKind.VideoSeries, true)]
    [InlineData(EntityKind.BookPage, false)]
    [InlineData(EntityKind.Collection, false)]
    [InlineData(EntityKind.Person, false)]
    [InlineData(EntityKind.Studio, false)]
    [InlineData(EntityKind.Tag, false)]
    public void SupportsFileDeletionMatchesManagedTreeRoots(EntityKind kind, bool expected) {
        Assert.Equal(expected, EntityKindRegistry.Describe(kind).SupportsFileDeletion);
    }

    [Fact]
    public void RegistryRoundTripsEveryKindByCodeAndType() {
        foreach (var kind in Enum.GetValues<EntityKind>()) {
            var descriptor = EntityKindRegistry.Describe(kind);
            Assert.Equal(kind, EntityKindRegistry.Require(descriptor.Code));
            if (descriptor.ClrType is not null) {
                Assert.Equal(kind, EntityKindRegistry.RequireType(descriptor.ClrType));
            }
        }
    }

    [Theory]
    [InlineData(EntityKind.Audio, "audio", "Audio", EntityKindCategory.Media, EntityStorageShape.File, null)]
    [InlineData(EntityKind.Movie, "movie", "Movie", EntityKindCategory.Media, EntityStorageShape.Folder, typeof(Prismedia.Domain.Media.Movie))]
    [InlineData(EntityKind.VideoSeries, "video-series", "Video Series", EntityKindCategory.Media, EntityStorageShape.Folder, typeof(Prismedia.Domain.Media.VideoSeries))]
    [InlineData(EntityKind.BookPage, "book-page", "Book Page", EntityKindCategory.Media, EntityStorageShape.ArchiveEntry, typeof(Prismedia.Domain.Media.BookPage))]
    [InlineData(EntityKind.Person, "person", "Person", EntityKindCategory.Taxonomy, EntityStorageShape.None, typeof(Prismedia.Domain.Taxonomy.Person))]
    [InlineData(EntityKind.AudioLibrary, "audio-library", "Audio Library", EntityKindCategory.Media, EntityStorageShape.Folder, typeof(Prismedia.Domain.Media.AudioLibrary))]
    public void DescriptorValuesArePreservedExactly(
        EntityKind kind,
        string code,
        string displayName,
        EntityKindCategory category,
        EntityStorageShape storageShape,
        Type? clrType) {
        var descriptor = EntityKindRegistry.Describe(kind);

        Assert.Equal(code, descriptor.Code);
        Assert.Equal(displayName, descriptor.DisplayName);
        Assert.Equal(category, descriptor.Category);
        Assert.Equal(storageShape, descriptor.StorageShape);
        Assert.Equal(clrType, descriptor.ClrType);
    }
}
