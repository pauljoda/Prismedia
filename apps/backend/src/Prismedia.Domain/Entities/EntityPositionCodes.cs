namespace Prismedia.Domain.Entities;

/// <summary>
/// Canonical codes for entity position rows (entity_positions.code) — the numeric orderings an entity
/// carries within its parent (a video's episode number, an album track's number). The single source of
/// truth for the position vocabulary: scan persistence, the metadata apply cascade, structural child
/// matching, and the request flow all reference these; provider wire spellings ("episodeNumber") are
/// normalized to them at the plugin boundary.
/// </summary>
public static class EntityPositionCodes {
    /// <summary>Season number of a video season (and of an episode, alongside its episode number).</summary>
    public const string Season = "season";

    /// <summary>Episode number of a video within its season.</summary>
    public const string Episode = "episode";

    /// <summary>Absolute episode number across all seasons (anime-style ordering).</summary>
    public const string AbsoluteEpisode = "absolute-episode";

    /// <summary>Track number of an audio track within its album.</summary>
    public const string Track = "track";

    /// <summary>Page number of a book page.</summary>
    public const string Page = "page";

    /// <summary>Chapter number of a book chapter.</summary>
    public const string Chapter = "chapter";

    /// <summary>Volume number of a book volume.</summary>
    public const string Volume = "volume";

    /// <summary>Generic sibling sort order when no medium-specific position applies.</summary>
    public const string Sort = "sort";
}
