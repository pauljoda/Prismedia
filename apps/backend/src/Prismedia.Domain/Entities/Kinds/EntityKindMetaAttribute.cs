namespace Prismedia.Domain.Entities;

/// <summary>
/// Declares the taxonomy metadata for an <see cref="EntityKind"/> member inline on the
/// member itself. The stable code comes from <see cref="CodeAttribute"/> (the same
/// mechanism every other enum uses); this attribute adds the kind-specific facts.
/// <see cref="EntityKindRegistry"/> builds itself from these by reflection, so there is
/// no hand-maintained descriptor table.
/// </summary>
/// <param name="category">Broad category used by metadata rows.</param>
/// <param name="storageShape">Filesystem storage shape used by scan and organize rules.</param>
/// <param name="groupLabel">Plural display label used when grouping entities by kind in
/// shared UI surfaces (e.g. "Videos", "Audio Tracks", "People").</param>
/// <param name="clrType">Concrete domain entity type, or null for kinds with no concrete type.</param>
/// <param name="enumeratesIdentifyChildren">Whether this kind is an identify <em>container</em> whose
/// local structural children are themselves separately identifiable works (e.g. a series' seasons, an
/// album's tracks). Leaf-content kinds (a movie, a standalone video, an image) leave this false so the
/// identify flow treats them as a single work and never walks into their own media file. The gate keys
/// off the <em>parent</em> kind: an episode is a <c>video</c> (false) but is still identified because
/// its parent season is a container.</param>
/// <param name="supportsFileDeletion">
/// Whether this kind is a safe root for the managed delete-files workflow. This is explicit rather
/// than inferred from <paramref name="storageShape"/>: a structural volume may own a safe folder or
/// archive path despite its <c>none</c> shape, while chapters and archive-entry pages must never delete
/// the archive that contains their virtual member path.
/// </param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EntityKindMetaAttribute(
    EntityKindCategory category,
    EntityStorageShape storageShape,
    string groupLabel,
    Type? clrType = null,
    bool enumeratesIdentifyChildren = false,
    bool supportsFileDeletion = false) : Attribute {
    /// <summary>Broad category for this kind.</summary>
    public EntityKindCategory Category { get; } = category;

    /// <summary>Filesystem storage shape for this kind.</summary>
    public EntityStorageShape StorageShape { get; } = storageShape;

    /// <summary>Plural display label used when grouping entities by kind in shared UI surfaces.</summary>
    public string GroupLabel { get; } = groupLabel;

    /// <summary>Concrete domain CLR type for this kind, or null when it has none.</summary>
    public Type? ClrType { get; } = clrType;

    /// <summary>
    /// Whether this kind is an identify container whose structural children are separately
    /// identifiable. See the constructor parameter for the full rule.
    /// </summary>
    public bool EnumeratesIdentifyChildren { get; } = enumeratesIdentifyChildren;

    /// <summary>Whether this kind can safely root the shared managed delete-files workflow.</summary>
    public bool SupportsFileDeletion { get; } = supportsFileDeletion;
}
