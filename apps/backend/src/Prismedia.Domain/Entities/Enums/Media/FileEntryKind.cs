namespace Prismedia.Domain.Entities;

/// <summary>
/// Filesystem node shape for a watched-root browse entry: a directory that can be expanded,
/// or a leaf file. Stored on <c>MediaFileIgnoreRow.Kind</c> and carried by the Files page
/// <c>FileEntry</c> contract.
/// </summary>
public enum FileEntryKind {
    /// <summary>Expandable directory node.</summary>
    [Code("directory")]
    Directory,

    /// <summary>Leaf file node.</summary>
    [Code("file")]
    File
}
