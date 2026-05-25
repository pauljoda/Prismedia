using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain model for a playable audio track.
/// </summary>
public sealed class AudioTrack : Entity {
    public AudioTrack(
        Guid id,
        string title,
        string? embeddedArtist,
        string? embeddedAlbum,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        EmbeddedArtist = embeddedArtist;
        EmbeddedAlbum = embeddedAlbum;
    }

    public override EntityKind Kind => EntityKind.AudioTrack;
    public string? EmbeddedArtist { get; private set; }
    public string? EmbeddedAlbum { get; private set; }

    /// <summary>
    /// Records a playback event on the attached playback capability.
    /// </summary>
    public void MarkPlayed(TimeSpan resumeTime, DateTimeOffset playedAt) {
        var playback = RequireCapability<CapabilityPlayback>();
        playback.MarkPlayed(resumeTime, playedAt);
    }

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityPlayback()
    ];
}
