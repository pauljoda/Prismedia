using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain model for a book author: a folder-backed grouping that gathers an author's
/// books (<see cref="Book"/> children) under one heading, mirroring how a
/// <see cref="MusicArtist"/> groups albums. Carries its own metadata; the books
/// themselves are parented to the author.
/// </summary>
public sealed class BookAuthor : Entity {
    /// <summary>
    /// Creates a book author grouping.
    /// </summary>
    /// <param name="id">Stable entity identity.</param>
    /// <param name="title">Display name of the author.</param>
    /// <param name="capabilities">Optional initial capability set.</param>
    public BookAuthor(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }

    /// <inheritdoc />
    public override EntityKind Kind => EntityKind.BookAuthor;

    /// <inheritdoc />
    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() => [];
}
