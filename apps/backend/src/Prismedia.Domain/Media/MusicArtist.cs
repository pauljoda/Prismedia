using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain model for a music artist or band: a folder-backed grouping that gathers an
/// artist's albums (<see cref="AudioLibrary"/> children) under one heading, much like a
/// <see cref="Gallery"/> groups images. Carries its own metadata and band members, which
/// are stored as person credits (<see cref="CapabilityCredits"/>) where the credit label
/// holds the member's role, e.g. "Drummer" or "Composer".
/// </summary>
public sealed class MusicArtist : Entity {
    /// <summary>
    /// Creates a music artist grouping.
    /// </summary>
    /// <param name="id">Stable entity identity.</param>
    /// <param name="title">Display name of the artist or band.</param>
    /// <param name="capabilities">Optional initial capability set.</param>
    public MusicArtist(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }

    /// <inheritdoc />
    public override EntityKind Kind => EntityKind.MusicArtist;

    /// <inheritdoc />
    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() => [];
}
