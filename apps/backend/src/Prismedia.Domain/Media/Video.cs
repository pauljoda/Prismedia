using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the playable video kind and its default media capabilities.</summary>
public sealed class VideoEntityKindDefinition() : EntityKindDefinition<Video>(
    EntityKind.Video,
    "video",
    "Video",
    "Videos",
    EntityKindCategory.Media,
    EntityStorageShape.File,
    defaultCapabilities: static () =>
    [
        new CapabilityPlayback(),
        new CapabilityPosition(),
        new CapabilityMarkers(),
        new CapabilitySubtitles(),
        new CapabilityCredits()
    ],
    supportsFileDeletion: true) {
    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Episode, "Episode", "Episodes", null, EntityKind.Video, EntityKind.Video,
            ProfileEntityKind: EntityKind.VideoSeries,
            LibraryRootMediaCapability: LibraryRootMediaCapability.ScanVideos,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true,
            AcquisitionKind: EntityKind.Video, Discoverable: false, AcquireFromEntity: true)
    ];
}

/// <summary>
/// Domain model for a playable video media item.
/// </summary>
public sealed class Video : Entity<VideoEntityKindDefinition> {
    public Video(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
