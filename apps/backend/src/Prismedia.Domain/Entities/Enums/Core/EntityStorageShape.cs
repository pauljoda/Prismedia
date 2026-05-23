namespace Prismedia.Domain.Entities;

/// <summary>
/// Filesystem storage shape used by scan and organize rules for an entity kind.
/// </summary>
public enum EntityStorageShape {
    /// <summary>Entity has no direct filesystem storage representation.</summary>
    [Code("none")]
    None,

    /// <summary>Entity owns a directory under its parent.</summary>
    [Code("folder")]
    Folder,

    /// <summary>Entity is represented by a regular file.</summary>
    [Code("file")]
    File,

    /// <summary>Entity is represented by an archive file.</summary>
    [Code("archive")]
    Archive,

    /// <summary>Entity is an addressable item inside an archive and is not moved independently.</summary>
    [Code("archive-entry")]
    ArchiveEntry
}
