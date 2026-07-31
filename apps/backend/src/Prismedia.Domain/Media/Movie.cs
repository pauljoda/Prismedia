using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the movie grouping kind and its shared metadata capabilities.</summary>
public sealed class MovieEntityKindDefinition() : RootEntityKindDefinition<Movie>(
    EntityKind.Movie,
    "movie",
    "Movie",
    "Movies",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
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
    static root => new Movie(root.Id, root.Title),
    new EntityKindBehavior(
        identification: new(
            AutoIdentifySelectorKind.Video,
            pluginFallbackKind: EntityKind.Video),
        manualAcquisition: EntityManualAcquisitionPolicy.UploadAndReplacement,
        engagement: new(
            EntityEngagementMode.Playback,
            aggregatesDirectChildPlayback: true,
            derivesCompletionFromPlaybackFraction: true),
        libraryVisibility: EntityLibraryVisibilityPolicy.FromDescendants(EntityKind.Video, 1),
        supportsFileDeletion: true,
        prunesWhenEmpty: true,
        mediaQualityFamily: EntityMediaQualityFamily.Video,
        supportsAtomicMediaUpgrade: true),
    defaultCapabilities: static () =>
    [
        new CapabilityDescription(),
        new CapabilityDates(),
        new CapabilitySource(),
        new CapabilityCredits()
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
        AcquisitionNamingFamily.Movie);
}

/// <summary>
/// Domain model for a single-film video release with one playable video child.
/// </summary>
public sealed class Movie : Entity<MovieEntityKindDefinition> {
    /// <summary>
    /// Creates a movie aggregate around one or more playable video children.
    /// </summary>
    /// <param name="id">Stable entity identifier.</param>
    /// <param name="title">Display title for the movie release.</param>
    /// <param name="videos">Playable video children that belong to this movie.</param>
    /// <param name="capabilities">Optional capability overrides loaded from persistence.</param>
    public Movie(
        Guid id,
        string title,
        IEnumerable<Entity>? videos = null,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        foreach (var video in videos ?? []) {
            AddChild(video);
        }
    }

    /// <summary>Playable video files that make up this movie release.</summary>
    public IReadOnlyList<Entity> Videos => ChildrenOf(EntityKind.Video);
}
