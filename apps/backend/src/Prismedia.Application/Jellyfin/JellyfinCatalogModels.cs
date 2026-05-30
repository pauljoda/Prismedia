namespace Prismedia.Application.Jellyfin;

/// <summary>Application query for Jellyfin-compatible item browsing.</summary>
public sealed record JellyfinItemQuery(
    Guid? ParentId,
    IReadOnlyList<Guid> Ids,
    bool Recursive,
    string? SearchTerm,
    IReadOnlyList<string> IncludeItemTypes,
    int StartIndex,
    int? Limit,
    string? SortBy,
    string? SortOrder,
    bool? IsFavorite,
    bool? IsPlayed);

/// <summary>Resolved image asset path for a Jellyfin image endpoint.</summary>
public sealed record JellyfinImageAsset(string Path, string ContentType, string ImageType, string ImageTag);
