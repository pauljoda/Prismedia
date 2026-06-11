namespace Prismedia.Domain.Entities;

/// <summary>
/// Why a watched-root path is skipped by library scans, stored on
/// <c>MediaFileIgnoreRow.Reason</c>.
/// </summary>
public enum MediaFileIgnoreReason {
    /// <summary>The user deleted the entity from the library and the file should not re-import.</summary>
    [Code("deleted-from-library")]
    DeletedFromLibrary,

    /// <summary>The user explicitly excluded the path from scans on the Files page.</summary>
    [Code("excluded-from-library")]
    ExcludedFromLibrary
}
