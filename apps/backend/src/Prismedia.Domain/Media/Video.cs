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
        DateTimeOffset? subtitlesExtractedAt,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        SubtitlesExtractedAt = subtitlesExtractedAt;
    }

    public override EntityKind Kind => EntityKind.Video;

    /// <summary>When embedded subtitles were last extracted, when known.</summary>
    public DateTimeOffset? SubtitlesExtractedAt { get; private set; }

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityRating(),
        new CapabilityLinks(),
        new CapabilityFlags(),
        new CapabilityFiles(),
        new CapabilityPlayback(),
        new CapabilityPosition(),
        new CapabilityMarkers(),
        new CapabilitySubtitles(),
        new CapabilityCredits()
    ];
}
