using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain model for a video series grouping.
/// </summary>
public sealed class VideoSeries : Entity {
    public VideoSeries(
        Guid id,
        string title,
        string? status = null,
        IEnumerable<Entity>? children = null,
        IEnumerable<Entity>? videos = null,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        Status = status;

        foreach (var child in children ?? []) {
            AddChild(child);
        }

        foreach (var video in videos ?? []) {
            AddChild(video);
        }
    }

    public override EntityKind Kind => EntityKind.VideoSeries;
    public string? Status { get; private set; }

    /// <summary>Direct child videos in insertion order.</summary>
    public IReadOnlyList<Entity> Videos => ChildrenOf(EntityKind.Video);

    /// <summary>Child seasons in insertion order.</summary>
    public IReadOnlyList<Entity> Seasons => ChildrenOf(EntityKind.VideoSeason);

    /// <summary>
    /// Layout for the series detail view, derived from whether the series has season children.
    /// </summary>
    public VideoSeriesRenderingMode RenderingMode =>
        Seasons.Count > 0 ? VideoSeriesRenderingMode.Seasons : VideoSeriesRenderingMode.Flat;

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityCredits()
    ];
}

/// <summary>
/// Structural video-season aggregate.
/// </summary>
public sealed class VideoSeason : Entity {
    public VideoSeason(
        Guid id,
        string title,
        Guid? parentEntityId,
        IEnumerable<EntityCapability>? capabilities = null,
        IEnumerable<Entity>? videos = null,
        int? sortOrder = null)
        : base(
            id,
            title,
            capabilities,
            parentEntityId: parentEntityId,
            sortOrder: sortOrder) {
        foreach (var video in videos ?? []) {
            AddChild(video);
        }
    }

    public override EntityKind Kind => EntityKind.VideoSeason;
    public IReadOnlyList<Entity> Videos => ChildrenOf(EntityKind.Video);

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityDescription(),
        new CapabilityDates(),
        new CapabilitySource(),
        new CapabilityPosition(),
        new CapabilityCredits()
    ];
}
