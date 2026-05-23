namespace Prismedia.Contracts.Settings;

/// <summary>
/// API-facing watched media root.
/// </summary>
public sealed record LibraryRoot(
    Guid Id,
    string Path,
    string Label,
    bool Enabled,
    bool Recursive,
    bool ScanVideos,
    bool ScanImages,
    bool ScanAudio,
    bool ScanBooks,
    bool IsNsfw,
    DateTimeOffset? LastScannedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request body for creating a watched media root.
/// </summary>
public sealed record LibraryRootCreateRequest(
    string Path,
    string? Label,
    bool? Enabled,
    bool? Recursive,
    bool? ScanVideos,
    bool? ScanImages,
    bool? ScanAudio,
    bool? ScanBooks,
    bool? IsNsfw);

/// <summary>
/// Request body for updating a watched media root.
/// </summary>
public sealed record LibraryRootUpdateRequest(
    string? Path,
    string? Label,
    bool? Enabled,
    bool? Recursive,
    bool? ScanVideos,
    bool? ScanImages,
    bool? ScanAudio,
    bool? ScanBooks,
    bool? IsNsfw);

/// <summary>
/// Directory entry used by the local folder browser.
/// </summary>
public sealed record LibraryBrowseEntry(string Name, string Path);

/// <summary>
/// Local folder browser response.
/// </summary>
public sealed record LibraryBrowseResponse(
    string Path,
    string? ParentPath,
    IReadOnlyList<LibraryBrowseEntry> Directories);
