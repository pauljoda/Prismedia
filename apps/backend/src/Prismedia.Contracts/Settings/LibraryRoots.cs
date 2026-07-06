namespace Prismedia.Contracts.Settings;

/// <summary>
/// API-facing watched media root.
/// </summary>
/// <param name="CreatedByUserId">User that created the root; null for pre-multi-user roots.</param>
/// <param name="AccessUserIds">
/// Member users granted access; populated on admin listings only (admins always see
/// every library and never appear here).
/// </param>
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
    DateTimeOffset UpdatedAt,
    bool AutoIdentify = true,
    Guid? CreatedByUserId = null,
    IReadOnlyList<Guid>? AccessUserIds = null);

/// <summary>
/// Member-facing summary of a library root the caller can access. Deliberately omits
/// host paths and scan configuration.
/// </summary>
public sealed record LibraryRootSummary(
    Guid Id,
    string Label,
    bool ScanVideos,
    bool ScanImages,
    bool ScanAudio,
    bool ScanBooks,
    bool IsNsfw);

/// <summary>
/// Request body for creating a watched media root.
/// </summary>
/// <param name="GrantUserIds">
/// Member users to grant access (admin callers only; member-created roots are always
/// creator-only regardless of this list).
/// </param>
public sealed record LibraryRootCreateRequest(
    string Path,
    string? Label,
    bool? Enabled,
    bool? Recursive,
    bool? ScanVideos,
    bool? ScanImages,
    bool? ScanAudio,
    bool? ScanBooks,
    bool? IsNsfw,
    bool? AutoIdentify = null,
    IReadOnlyList<Guid>? GrantUserIds = null);

/// <summary>Request replacing the member users granted to one library root.</summary>
public sealed record LibraryAccessUpdateRequest(IReadOnlyList<Guid> UserIds);

/// <summary>Request replacing the library roots granted to one member user.</summary>
public sealed record UserLibraryAccessUpdateRequest(IReadOnlyList<Guid> LibraryRootIds);

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
    bool? IsNsfw,
    bool? AutoIdentify = null);

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
