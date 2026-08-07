namespace Prismedia.Domain.Entities;

/// <summary>
/// Shared semantic icon represented by an Entity kind. Clients translate these stable concepts to
/// their platform icon libraries; definitions never name Lucide symbols, SF Symbols, or other
/// presentation-framework details.
/// </summary>
public enum EntityKindIcon {
    [Code("album")]
    Album,

    [Code("artist")]
    Artist,

    [Code("audio")]
    Audio,

    [Code("author")]
    Author,

    [Code("book")]
    Book,

    [Code("chapter")]
    Chapter,

    [Code("collection")]
    Collection,

    [Code("gallery")]
    Gallery,

    [Code("image")]
    Image,

    [Code("movie")]
    Movie,

    [Code("page")]
    Page,

    [Code("person")]
    Person,

    [Code("season")]
    Season,

    [Code("series")]
    Series,

    [Code("studio")]
    Studio,

    [Code("tag")]
    Tag,

    [Code("track")]
    Track,

    [Code("video")]
    Video,

    [Code("volume")]
    Volume
}

/// <summary>Named hue in Prismedia's shared entity spectrum.</summary>
public enum EntityAccentHue {
    [Code("red")]
    Red,

    [Code("orange")]
    Orange,

    [Code("yellow")]
    Yellow,

    [Code("green")]
    Green,

    [Code("cyan")]
    Cyan,

    [Code("blue")]
    Blue,

    [Code("violet")]
    Violet,

    [Code("magenta")]
    Magenta
}

/// <summary>Default artwork scaling behavior inside an Entity thumbnail frame.</summary>
public enum EntityArtworkFit {
    [Code("cover")]
    Cover,

    [Code("contain")]
    Contain
}

/// <summary>Background treatment applied by clients around an Entity's original artwork.</summary>
public enum EntityArtworkSurface {
    [Code("plain")]
    Plain,

    [Code("brand-plate")]
    BrandPlate
}

/// <summary>
/// Platform-neutral presentation facts every Entity kind must define. Exact aspect-ratio values
/// avoid parallel client shape registries, while semantic icons and hues let each UI retain its
/// native rendering implementation.
/// </summary>
public sealed record EntityKindPresentation {
    /// <summary>Creates validated immutable presentation metadata.</summary>
    public EntityKindPresentation(
        EntityKindIcon icon,
        EntityKindIcon referenceIcon,
        int thumbnailWidth,
        int thumbnailHeight,
        EntityAccentHue primaryAccent,
        EntityAccentHue secondaryAccent,
        EntityArtworkFit artworkFit,
        EntityArtworkSurface artworkSurface = EntityArtworkSurface.Plain,
        bool usesRepresentativeChildArtwork = false,
        IEnumerable<EntityKind>? borrowArtworkFromParentKinds = null) {
        if (thumbnailWidth <= 0) {
            throw new ArgumentOutOfRangeException(nameof(thumbnailWidth), "Thumbnail width must be positive.");
        }
        if (thumbnailHeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(thumbnailHeight), "Thumbnail height must be positive.");
        }
        var parentKinds = borrowArtworkFromParentKinds?.ToArray() ?? [];
        if (parentKinds.Distinct().Count() != parentKinds.Length) {
            throw new ArgumentException(
                "Borrowed parent artwork kinds cannot contain duplicates.",
                nameof(borrowArtworkFromParentKinds));
        }

        Icon = icon;
        ReferenceIcon = referenceIcon;
        ThumbnailWidth = thumbnailWidth;
        ThumbnailHeight = thumbnailHeight;
        PrimaryAccent = primaryAccent;
        SecondaryAccent = secondaryAccent;
        ArtworkFit = artworkFit;
        ArtworkSurface = artworkSurface;
        UsesRepresentativeChildArtwork = usesRepresentativeChildArtwork;
        BorrowArtworkFromParentKinds = Array.AsReadOnly(parentKinds);
    }

    /// <summary>Specific semantic icon for representing an Entity of this kind.</summary>
    public EntityKindIcon Icon { get; }

    /// <summary>
    /// Broader icon used when aggregating reference counts, allowing related kinds such as movies,
    /// series, and videos to merge into one video count chip.
    /// </summary>
    public EntityKindIcon ReferenceIcon { get; }

    /// <summary>Canonical thumbnail width component.</summary>
    public int ThumbnailWidth { get; }

    /// <summary>Canonical thumbnail height component.</summary>
    public int ThumbnailHeight { get; }

    /// <summary>Primary muted spectrum hue.</summary>
    public EntityAccentHue PrimaryAccent { get; }

    /// <summary>Secondary muted spectrum hue.</summary>
    public EntityAccentHue SecondaryAccent { get; }

    /// <summary>Default scaling behavior for artwork within the thumbnail frame.</summary>
    public EntityArtworkFit ArtworkFit { get; }

    /// <summary>
    /// Client-rendered surface surrounding the untouched source artwork. This lets transparent
    /// logos remain in their original format while sharing one readable treatment everywhere.
    /// </summary>
    public EntityArtworkSurface ArtworkSurface { get; }

    /// <summary>Whether a missing cover falls back to the first representative child image.</summary>
    public bool UsesRepresentativeChildArtwork { get; }

    /// <summary>Parent kinds whose cover may be shown when this kind has no cover of its own.</summary>
    public IReadOnlyList<EntityKind> BorrowArtworkFromParentKinds { get; }
}
