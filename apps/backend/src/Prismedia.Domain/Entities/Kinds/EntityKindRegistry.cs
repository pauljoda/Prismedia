using System.Reflection;
using System.Text.RegularExpressions;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Broad category for an entity kind when infrastructure needs seed metadata.
/// </summary>
public enum EntityKindCategory {
    /// <summary>Playable or viewable media.</summary>
    Media,

    /// <summary>Taxonomy or organization entity.</summary>
    Taxonomy,

    /// <summary>User-curated grouping entity.</summary>
    Collection
}

/// <summary>
/// Resolved facts for one entity kind: its stable code, display name, plural group
/// label, category, filesystem storage shape, and concrete domain CLR type (when one
/// exists).
/// </summary>
/// <param name="Value">Domain enum value.</param>
/// <param name="Code">Stable database/API code.</param>
/// <param name="DisplayName">Human-readable singular display name.</param>
/// <param name="GroupLabel">Plural display label used when grouping entities by kind.</param>
/// <param name="Category">Broad category used by metadata rows.</param>
/// <param name="StorageShape">Filesystem storage shape used by scan and organize rules.</param>
/// <param name="ClrType">Concrete domain entity type, or null for kinds with no concrete type.</param>
/// <param name="EnumeratesIdentifyChildren">Whether this kind is an identify container whose local
/// structural children are separately identifiable works (e.g. a series' seasons, an album's tracks);
/// leaf-content kinds such as a movie leave this false.</param>
public sealed record EntityKindDescriptor(
    EntityKind Value,
    string Code,
    string DisplayName,
    string GroupLabel,
    EntityKindCategory Category,
    EntityStorageShape StorageShape,
    Type? ClrType,
    bool EnumeratesIdentifyChildren) {
    /// <summary>Allows descriptors to flow into domain-only metadata APIs.</summary>
    public static implicit operator EntityKind(EntityKindDescriptor descriptor) => descriptor.Value;
}

/// <summary>
/// The single registry for the entity-kind taxonomy. It builds itself by reflecting the
/// inline <see cref="CodeAttribute"/> (via the shared codec) and
/// <see cref="EntityKindMetaAttribute"/> on each <see cref="EntityKind"/> member, so
/// there is no hand-maintained descriptor table — adding a kind is just the enum member
/// plus its two attributes.
/// </summary>
public static class EntityKindRegistry {
    private static readonly IReadOnlyList<EntityKindDescriptor> Descriptors =
        Enum.GetValues<EntityKind>().Select(Build).ToArray();

    private static readonly IReadOnlyDictionary<EntityKind, EntityKindDescriptor> ByKind =
        Descriptors.ToDictionary(descriptor => descriptor.Value);

    private static readonly IReadOnlyDictionary<string, EntityKindDescriptor> ByCode =
        Descriptors.ToDictionary(descriptor => descriptor.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<Type, EntityKindDescriptor> ByType =
        Descriptors.Where(descriptor => descriptor.ClrType is not null)
            .ToDictionary(descriptor => descriptor.ClrType!);

    /// <summary>All known entity kind descriptors.</summary>
    public static IReadOnlyList<EntityKindDescriptor> All => Descriptors;

    public static EntityKindDescriptor Audio => Describe(EntityKind.Audio);
    public static EntityKindDescriptor AudioLibrary => Describe(EntityKind.AudioLibrary);
    public static EntityKindDescriptor AudioTrack => Describe(EntityKind.AudioTrack);
    public static EntityKindDescriptor Book => Describe(EntityKind.Book);
    public static EntityKindDescriptor BookAuthor => Describe(EntityKind.BookAuthor);
    public static EntityKindDescriptor BookVolume => Describe(EntityKind.BookVolume);
    public static EntityKindDescriptor BookChapter => Describe(EntityKind.BookChapter);
    public static EntityKindDescriptor BookPage => Describe(EntityKind.BookPage);
    public static EntityKindDescriptor Collection => Describe(EntityKind.Collection);
    public static EntityKindDescriptor Gallery => Describe(EntityKind.Gallery);
    public static EntityKindDescriptor Image => Describe(EntityKind.Image);
    public static EntityKindDescriptor Movie => Describe(EntityKind.Movie);
    public static EntityKindDescriptor MusicArtist => Describe(EntityKind.MusicArtist);
    public static EntityKindDescriptor Person => Describe(EntityKind.Person);
    public static EntityKindDescriptor Studio => Describe(EntityKind.Studio);
    public static EntityKindDescriptor Tag => Describe(EntityKind.Tag);
    public static EntityKindDescriptor Video => Describe(EntityKind.Video);
    public static EntityKindDescriptor VideoSeries => Describe(EntityKind.VideoSeries);
    public static EntityKindDescriptor VideoSeason => Describe(EntityKind.VideoSeason);

    /// <summary>Gets the full descriptor for a domain entity kind.</summary>
    public static EntityKindDescriptor Describe(EntityKind kind) => ByKind[kind];

    /// <summary>
    /// Whether a kind (by stable code) is an identify container whose local structural children
    /// should be separately identified during a cascade. Unknown codes are treated as leaves.
    /// </summary>
    public static bool EnumeratesIdentifyChildren(string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        ByCode.TryGetValue(code, out var descriptor) &&
        descriptor.EnumeratesIdentifyChildren;

    /// <summary>Encodes a domain entity kind to its stable storage code.</summary>
    public static string ToCode(EntityKind kind) => ByKind[kind].Code;

    /// <summary>Decodes a storage code to a domain entity kind.</summary>
    public static EntityKind Require(string code) =>
        TryGet(code, out var kind)
            ? kind
            : throw new InvalidOperationException($"Unknown entity kind code '{code}'.");

    /// <summary>Gets the entity kind represented by a concrete domain entity CLR type.</summary>
    public static EntityKind RequireType(Type entityType) {
        ArgumentNullException.ThrowIfNull(entityType);
        return ByType.TryGetValue(entityType, out var descriptor)
            ? descriptor.Value
            : throw new InvalidOperationException($"Entity type '{entityType.Name}' is not registered.");
    }

    /// <summary>Attempts to decode a storage code to a domain entity kind.</summary>
    public static bool TryGet(string code, out EntityKind kind) {
        if (!string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code, out var descriptor)) {
            kind = descriptor.Value;
            return true;
        }

        kind = default;
        return false;
    }

    private static EntityKindDescriptor Build(EntityKind value) {
        var name = value.ToString();
        var field = typeof(EntityKind).GetField(name)!;
        var meta = field.GetCustomAttribute<EntityKindMetaAttribute>()
            ?? throw new InvalidOperationException($"EntityKind.{name} is missing an [EntityKindMeta] attribute.");

        return new EntityKindDescriptor(
            value,
            CodecRegistry.Get<EntityKind>().Encode(value),
            Regex.Replace(name, "(?<=[a-z])([A-Z])", " $1"),
            meta.GroupLabel,
            meta.Category,
            meta.StorageShape,
            meta.ClrType,
            meta.EnumeratesIdentifyChildren);
    }
}
