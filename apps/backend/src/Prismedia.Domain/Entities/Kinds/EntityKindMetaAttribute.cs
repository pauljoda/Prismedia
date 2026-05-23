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
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EntityKindMetaAttribute(
    EntityKindCategory category,
    EntityStorageShape storageShape,
    string groupLabel,
    Type? clrType = null) : Attribute {
    /// <summary>Broad category for this kind.</summary>
    public EntityKindCategory Category { get; } = category;

    /// <summary>Filesystem storage shape for this kind.</summary>
    public EntityStorageShape StorageShape { get; } = storageShape;

    /// <summary>Plural display label used when grouping entities by kind in shared UI surfaces.</summary>
    public string GroupLabel { get; } = groupLabel;

    /// <summary>Concrete domain CLR type for this kind, or null when it has none.</summary>
    public Type? ClrType { get; } = clrType;
}
