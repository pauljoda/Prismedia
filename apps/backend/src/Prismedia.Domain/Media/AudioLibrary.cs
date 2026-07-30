using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the audio-library kind, defaults, and shared-root construction.</summary>
public sealed class AudioLibraryEntityKindDefinition() : RootEntityKindDefinition<AudioLibrary>(
    EntityKind.AudioLibrary,
    "audio-library",
    "Audio Library",
    "Audio Libraries",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    static root => new AudioLibrary(root.Id, root.Title),
    enumeratesIdentifyChildren: true,
    supportsFileDeletion: true);

/// <summary>
/// Domain model for an album, audiobook, podcast, or other audio grouping.
/// </summary>
public sealed class AudioLibrary : Entity<AudioLibraryEntityKindDefinition> {
    public AudioLibrary(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
