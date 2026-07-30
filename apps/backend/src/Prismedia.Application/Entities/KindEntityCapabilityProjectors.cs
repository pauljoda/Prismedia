using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;

namespace Prismedia.Application.Entities;

[EntityCapabilityProjector(220)]
internal sealed class BookMetadataCapabilityProjector : EntityCapabilityProjector<Book, BookMetadataCapability> {
    protected override BookMetadataCapability Project(
        Book entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.BookType, entity.Format);
}

[EntityCapabilityProjector(230)]
internal sealed class BookCoverSelectionCapabilityProjector : EntityCapabilityProjector<Book, CoverSelectionCapability> {
    protected override CoverSelectionCapability Project(
        Book entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.CoverPageId);
}

[EntityCapabilityProjector(240)]
internal sealed class BookChapterCoverSelectionCapabilityProjector
    : EntityCapabilityProjector<BookChapter, CoverSelectionCapability> {
    protected override CoverSelectionCapability Project(
        BookChapter entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.CoverPageId);
}

[EntityCapabilityProjector(250)]
internal sealed class GalleryMetadataCapabilityProjector
    : EntityCapabilityProjector<Gallery, GalleryMetadataCapability> {
    protected override GalleryMetadataCapability Project(
        Gallery entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.GalleryType);
}

[EntityCapabilityProjector(260)]
internal sealed class GalleryCoverSelectionCapabilityProjector
    : EntityCapabilityProjector<Gallery, CoverSelectionCapability> {
    protected override CoverSelectionCapability Project(
        Gallery entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.CoverImageId);
}

[EntityCapabilityProjector(270)]
internal sealed class PersonProfileCapabilityProjector
    : EntityCapabilityProjector<Person, PersonProfileCapability> {
    protected override PersonProfileCapability Project(
        Person entity,
        EntityCapabilityProjectionContext context) =>
        new(
            entity.Disambiguation,
            entity.Gender,
            entity.Country,
            entity.Ethnicity,
            entity.EyeColor,
            entity.HairColor,
            entity.Height,
            entity.Weight,
            entity.Measurements,
            entity.Tattoos,
            entity.Piercings);
}

[EntityCapabilityProjector(280)]
internal sealed class EmbeddedAudioMetadataCapabilityProjector
    : EntityCapabilityProjector<AudioTrack, EmbeddedAudioMetadataCapability> {
    protected override EmbeddedAudioMetadataCapability Project(
        AudioTrack entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.EmbeddedArtist, entity.EmbeddedAlbum);
}

[EntityCapabilityProjector(290)]
internal sealed class TagPolicyCapabilityProjector : EntityCapabilityProjector<Tag, TagPolicyCapability> {
    protected override TagPolicyCapability Project(
        Tag entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.IgnoreAutoTag);
}

[EntityCapabilityProjector(300)]
internal sealed class CollectionConfigurationCapabilityProjector
    : EntityCapabilityProjector<Collection, CollectionConfigurationCapability> {
    protected override CollectionConfigurationCapability Project(
        Collection entity,
        EntityCapabilityProjectionContext context) =>
        new(
            entity.IsShared,
            context.CurrentUserId is { } userId && entity.IsOwnedBy(userId),
            entity.Mode,
            entity.RuleTreeJson,
            entity.CoverMode,
            entity.LastRefreshedAt);
}

[EntityCapabilityProjector(310)]
internal sealed class CollectionCoverSelectionCapabilityProjector
    : EntityCapabilityProjector<Collection, CoverSelectionCapability> {
    protected override CoverSelectionCapability Project(
        Collection entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.CoverItemId);
}

[EntityCapabilityProjector(320)]
internal sealed class SeriesMetadataCapabilityProjector
    : EntityCapabilityProjector<VideoSeries, SeriesMetadataCapability> {
    protected override SeriesMetadataCapability Project(
        VideoSeries entity,
        EntityCapabilityProjectionContext context) =>
        new(entity.Status);
}
