namespace Prismedia.Application.Acquisition;

// The on-disk targets IImportTargetIndex resolves, so an import merges into an existing folder tree.

/// <summary>
/// One season's owned episode files, independent of structural folder provenance. A null folder means
/// new episodes use the configured season template; existing files retain their exact paths and coverage.
/// </summary>
public sealed record TvSeasonDiskLayout(
    Guid SeasonEntityId,
    string? FolderPath,
    IReadOnlyDictionary<int, string> EpisodeFileByNumber) {
    /// <summary>Duplicate seasons or unnumbered owned files prevent a safe interpretation of this season's coverage.</summary>
    public bool HasUnresolvedOwnership { get; init; }

    /// <summary>Episode numbers with competing entities or sources, excluded from the unambiguous owned-file map.</summary>
    public IReadOnlySet<int> AmbiguousEpisodeNumbers { get; init; } = new HashSet<int>();
}

/// <summary>An existing on-disk series' folder layout: the series folder and its seasons keyed by season number.</summary>
public sealed record TvSeriesDiskLayout(
    Guid SeriesEntityId,
    string SeriesFolderPath,
    IReadOnlyDictionary<int, TvSeasonDiskLayout> Seasons) {
    /// <summary>Physical paths with unresolved ownership, including owners outside the unambiguous episode map.</summary>
    public IReadOnlySet<string> UnresolvedSourcePaths { get; init; } = new HashSet<string>();
}

/// <summary>An existing on-disk movie: its folder and the owned video file when one exists.</summary>
public sealed record MovieDiskTarget(Guid MovieEntityId, string FolderPath, string? OwnedSourceFilePath);

/// <summary>
/// An existing on-disk album target: the album folder when the album owns one, the artist folder when
/// only the grouping exists on disk, and the album's already-owned files (relative to the album folder).
/// </summary>
public sealed record AlbumDiskTarget(
    Guid AlbumEntityId,
    string? AlbumFolderPath,
    string? ArtistFolderPath,
    IReadOnlySet<string> ExistingRelativeFiles);
