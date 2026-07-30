using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the playable audio-track kind and its default playback capability.</summary>
public sealed class AudioTrackEntityKindDefinition() : EntityKindDefinition<AudioTrack>(
    EntityKind.AudioTrack,
    "audio-track",
    "Audio Track",
    "Audio Tracks",
    EntityKindCategory.Media,
    EntityStorageShape.File,
    defaultCapabilities: static () => [new CapabilityPlayback()],
    supportsFileDeletion: true);

/// <summary>
/// Domain model for a playable audio track.
/// </summary>
public sealed class AudioTrack : Entity<AudioTrackEntityKindDefinition> {
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

    public string? EmbeddedArtist { get; private set; }
    public string? EmbeddedAlbum { get; private set; }

    /// <summary>
    /// Records a playback event on the attached playback capability.
    /// </summary>
    public void MarkPlayed(TimeSpan resumeTime, DateTimeOffset playedAt) {
        var playback = RequireCapability<CapabilityPlayback>();
        playback.MarkPlayed(resumeTime, playedAt);
    }
}
