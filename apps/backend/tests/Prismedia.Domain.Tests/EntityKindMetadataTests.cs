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
