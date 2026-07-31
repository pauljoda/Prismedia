using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the directly playable movie kind and its release metadata capabilities.</summary>
public sealed class MovieEntityKindDefinition() : PlayableVideoEntityKindDefinition<Movie>(
    EntityKind.Movie,
    "movie",
    "Movie",
    "Movies",
    new EntityKindPresentation(
        EntityKindIcon.Movie,
        EntityKindIcon.Video,
        2,
        3,
        EntityAccentHue.Orange,
        EntityAccentHue.Yellow,
        EntityArtworkFit.Cover),
    new EntityKindNavigation(EntityKind.Movie, "movies", "/movies", "/movies/{id}"),
    new EntityKindSearch(0),
    PlayableVideoScanPlacement.Movie,
    static root => new Movie(root.Id, root.Title),
    identification: new(
        AutoIdentifySelectorKind.Video,
        pluginFallbackKind: EntityKind.Video),
    manualAcquisition: EntityManualAcquisitionPolicy.UploadAndReplacement,
    browse: null,
    libraryVisibility: EntityLibraryVisibilityPolicy.DirectRoot,
    additionalDefaultCapabilities: static () =>
    [
        new CapabilityDescription(),
        new CapabilityDates(),
        new CapabilitySource()
    ]) {
    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Movie, "Movie", "Movies", null, EntityKind.Movie, EntityKind.Movie,
            ProfileEntityKind: EntityKind.Movie,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true,
            AcquisitionKind: EntityKind.Movie)
    ];

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy => EntityStructurePolicy.RootOnly;

    /// <inheritdoc />
    public override AcquisitionProfileDefinition AcquisitionProfile { get; } = new(
        "Movies",
        1,
        LibraryRootMediaCapability.ScanVideos,
        [
            EntityDateType.Premiere,
            EntityDateType.TheatricalRelease,
            EntityDateType.StreamingRelease,
            EntityDateType.DigitalRelease,
            EntityDateType.PhysicalRelease,
            EntityDateType.Release
        ],
        "{Title} ({Year})/{Title} ({Year}).{ext}",
        "{Title} {Year} {Quality} {ext} — 2 segments: folder/file",
        AcquisitionNamingFamily.Movie,
        AcquisitionCheckpointProtocol.Placement);
}

/// <summary>
/// Domain model for a directly playable single-film video release.
/// </summary>
public sealed class Movie : Entity<MovieEntityKindDefinition> {
    /// <summary>Creates a directly playable movie.</summary>
    /// <param name="id">Stable entity identifier.</param>
    /// <param name="title">Display title for the movie release.</param>
    /// <param name="capabilities">Optional capability overrides loaded from persistence.</param>
    public Movie(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
