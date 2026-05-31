using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain model for a playable video media item.
/// </summary>
public sealed class Video : Entity {
    public Video(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }

    public override EntityKind Kind => EntityKind.Video;

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityPlayback(),
        new CapabilityPosition(),
        new CapabilityMarkers(),
        new CapabilitySubtitles(),
        new CapabilityCredits()
    ];
}
