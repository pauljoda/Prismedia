using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>The identity a media naming template renders into a target path.</summary>
/// <param name="Title">The movie title, or the album title, or the episode-series title as appropriate.</param>
/// <param name="Series">The series name for TV templates (falls back to <paramref name="Title"/> when blank).</param>
/// <param name="Artist">The artist name for music templates.</param>
/// <param name="Album">The album name for music templates.</param>
/// <param name="Year">The release year, or null when unknown (year decorations degrade cleanly).</param>
/// <param name="Season">The season number for a rendered episode file.</param>
/// <param name="Episode">The episode number for a rendered episode file.</param>
/// <param name="Quality">The detected quality code of the selected release, or empty/null when unknown.</param>
/// <param name="Extension">The file extension of the placed file (with or without a leading dot).</param>
public sealed record MediaNamingContext(
    string Title,
    string? Series = null,
    string? Artist = null,
    string? Album = null,
    int? Year = null,
    int? Season = null,
    int? Episode = null,
    string? Quality = null,
    string? Extension = null);

/// <summary>
/// Pure per-kind naming templates for movie, TV, and music imports. A profile stores one path template per
/// kind (in the shared <c>PathTemplate</c> column); this renderer turns it into a sanitized library-relative
/// path, supplies the per-kind default that reproduces the historical hardcoded layout, and validates a
/// template before it is saved.
///
/// The STRUCTURE of every template is constrained because the post-import scan binds imported files to
/// their wanted entities by folder layout (a series by its ancestor folder, an album by its
/// <c>{artist}/{album}</c> folder, a movie by its folder). Only the NAMES inside each segment are
/// user-configurable — the segment count and which segment carries which unit are fixed per kind so a
/// template can never break binding.
/// </summary>
public static partial class MediaNamingTemplates {
    /// <summary>The movie default: <c>{Title} ({Year})/{Title} ({Year}).{ext}</c> — the historical hardcoded layout.</summary>
    public const string MovieDefault = "{Title} ({Year})/{Title} ({Year}).{ext}";

    /// <summary>The TV default: <c>{Series}/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}</c> — the historical layout.</summary>
    public const string TvDefault = "{Series}/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}";

    /// <summary>The music default: <c>{Artist}/{Album}</c> — the album folder only (track files keep their release names).</summary>
    public const string MusicDefault = "{Artist}/{Album}";

    [GeneratedRegex(@"\{Season(?::0+)?\}|\{Season\}", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonTokenRegex();

    [GeneratedRegex(@"\{Episode(?::0+)?\}|\{Episode\}", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeTokenRegex();

    /// <summary>The default naming template for a profile kind, or null for kinds without a media template (books own theirs).</summary>
    public static string? DefaultFor(EntityKind kind) => AcquisitionProfileKinds.For(kind) switch {
        EntityKind.Movie => MovieDefault,
        EntityKind.VideoSeries => TvDefault,
        EntityKind.AudioLibrary => MusicDefault,
        _ => null,
    };

    /// <summary>True when <paramref name="kind"/> is a media kind this renderer governs (movie, TV, music).</summary>
    public static bool IsMediaKind(EntityKind kind) => DefaultFor(kind) is not null;

    /// <summary>
    /// Validates a template for a media kind, returning a human-readable error message, or null when the
    /// template is valid. Enforces the per-kind segment count, the required unit tokens per segment, and
    /// rejects traversal and segments that render empty for a fully-populated context.
    /// </summary>
    public static string? Validate(EntityKind kind, string template) {
        var profileKind = AcquisitionProfileKinds.For(kind);
        if (string.IsNullOrWhiteSpace(template)) {
            return "A naming template is required.";
        }

        var segments = template.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment.Contains("..", StringComparison.Ordinal))) {
            return "A naming template may not contain path traversal (..).";
        }

        // A blank segment (a leading, trailing, or doubled slash) has no name to sanitize.
        if (segments.Any(string.IsNullOrWhiteSpace)) {
            return "A naming template segment may not be empty.";
        }

        return profileKind switch {
            EntityKind.Movie => ValidateMovie(segments),
            EntityKind.VideoSeries => ValidateTv(segments),
            EntityKind.AudioLibrary => ValidateMusic(segments),
            _ => "Naming templates apply to movies, TV, and music.",
        };
    }

    private static string? ValidateMovie(string[] segments) {
        if (segments.Length != 2) {
            return "A movie template must have exactly 2 segments: a folder and a file (e.g. \"{Title} ({Year})/{Title} ({Year}).{ext}\").";
        }

        // A fully-populated sample must render a non-empty name in every segment.
        var sample = new MediaNamingContext("Title", Year: 2020, Quality: "bluray-1080p", Extension: ".mkv");
        return SampleRendersEveryFolderSegment(segments, sample);
    }

    private static string? ValidateTv(string[] segments) {
        if (segments.Length != 3) {
            return "A TV template must have exactly 3 segments: series/season/episode (e.g. \"{Series}/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}\").";
        }

        if (!segments[0].Contains("{Series}", StringComparison.OrdinalIgnoreCase)) {
            return "The first TV segment (the series folder) must contain {Series}.";
        }

        if (!SeasonTokenRegex().IsMatch(segments[1])) {
            return "The second TV segment (the season folder) must contain a season token ({Season} or {Season:00}).";
        }

        if (!EpisodeTokenRegex().IsMatch(segments[2])) {
            return "The third TV segment (the episode file) must contain an episode token ({Episode:00}).";
        }

        var sample = new MediaNamingContext("Series", Series: "Series", Year: 2020, Season: 1, Episode: 1, Quality: "bluray-1080p", Extension: ".mkv");
        return SampleRendersEveryFolderSegment(segments, sample);
    }

    private static string? ValidateMusic(string[] segments) {
        if (segments.Length != 2) {
            return "A music template must have exactly 2 segments: artist/album (e.g. \"{Artist}/{Album}\").";
        }

        if (!segments[0].Contains("{Artist}", StringComparison.OrdinalIgnoreCase)) {
            return "The first music segment (the artist folder) must contain {Artist}.";
        }

        if (!segments[1].Contains("{Album}", StringComparison.OrdinalIgnoreCase)) {
            return "The second music segment (the album folder) must contain {Album}.";
        }

        var sample = new MediaNamingContext("Album", Artist: "Artist", Album: "Album");
        return SampleRendersEveryFolderSegment(segments, sample);
    }

    /// <summary>Guards that every segment renders a non-empty sanitized name for a populated context.</summary>
    private static string? SampleRendersEveryFolderSegment(string[] segments, MediaNamingContext sample) {
        foreach (var segment in segments) {
            if (string.IsNullOrEmpty(RenderSegment(segment, sample))) {
                return "A naming template segment renders empty; every segment must contain at least one token or literal text.";
            }
        }

        return null;
    }

    /// <summary>
    /// Renders the movie template to a sanitized library-relative path (folder/file). Falls back to
    /// <see cref="MovieDefault"/> when <paramref name="template"/> is blank or invalid, so a stored-but-bad
    /// template never fails an import.
    /// </summary>
    public static string RenderMoviePath(string? template, MediaNamingContext context) =>
        RenderPath(ResolveTemplate(EntityKind.Movie, template), context);

    /// <summary>Renders the movie folder segment only (used for the scan hint), from the same template.</summary>
    public static string RenderMovieFolder(string? template, MediaNamingContext context) =>
        FolderOf(RenderMoviePath(template, context));

    /// <summary>
    /// Renders one TV episode's target path (series/season/episode) from the template, falling back to
    /// <see cref="TvDefault"/> when blank or invalid.
    /// </summary>
    public static string RenderTvPath(string? template, MediaNamingContext context) =>
        RenderPath(ResolveTemplate(EntityKind.VideoSeries, template), context);

    /// <summary>The series folder segment (segment 1) a TV template places into — the scan binds a series by it.</summary>
    public static string RenderTvSeriesFolder(string? template, MediaNamingContext context) =>
        RenderSegment(FirstSegment(ResolveTemplate(EntityKind.VideoSeries, template)), context);

    /// <summary>The season folder path (segments 1..2) a TV template places into — the scan hint keys on it for a single season.</summary>
    public static string RenderTvSeasonFolder(string? template, MediaNamingContext context) {
        var resolved = ResolveTemplate(EntityKind.VideoSeries, template);
        var segments = resolved.Split('/', StringSplitOptions.None);
        // The season folder is everything up to (but not including) the episode file segment.
        return RenderPath(string.Join('/', segments[..^1]), context);
    }

    /// <summary>
    /// The <c>{artist}/{album}</c> album folder a music template places into, falling back to
    /// <see cref="MusicDefault"/> when blank or invalid. Track files keep their release names and inner
    /// disc structure — renaming individual tracks is out of scope; the template controls the folder only.
    /// </summary>
    public static string RenderMusicAlbumFolder(string? template, MediaNamingContext context) =>
        RenderPath(ResolveTemplate(EntityKind.AudioLibrary, template), context);

    /// <summary>Renders a template to a sanitized relative path, dropping any segment that renders empty.</summary>
    private static string RenderPath(string template, MediaNamingContext context) {
        var segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rendered = new List<string>(segments.Length);
        foreach (var segment in segments) {
            var value = RenderSegment(segment, context);
            if (!string.IsNullOrEmpty(value)) {
                rendered.Add(value);
            }
        }

        return string.Join('/', rendered);
    }

    /// <summary>Substitutes one segment's tokens, cleans empty decorations, and sanitizes it for placement.</summary>
    private static string RenderSegment(string segment, MediaNamingContext context) {
        var substituted = ReplaceTokens(segment, context);
        return ImportPlanBuilder.SanitizeSegment(ImportPlanBuilder.CleanEmptyDecorations(substituted));
    }

    private static string ReplaceTokens(string segment, MediaNamingContext context) {
        var result = segment
            .Replace("{Series}", context.Series ?? context.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{Artist}", context.Artist ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{Album}", context.Album ?? context.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{Title}", context.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{Year}", context.Year?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{Quality}", context.Quality ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", (context.Extension ?? string.Empty).TrimStart('.'), StringComparison.OrdinalIgnoreCase);

        // Numbered variants render only when the unit is present; a missing unit leaves the token empty so
        // its decorations collapse (matching the year/quality behavior).
        result = ReplaceNumberToken(result, "Season", context.Season);
        result = ReplaceNumberToken(result, "Episode", context.Episode);
        return result;
    }

    /// <summary>Replaces the padded (<c>{Name:00}</c>) and unpadded (<c>{Name}</c>) forms of a numeric token.</summary>
    private static string ReplaceNumberToken(string input, string name, int? value) {
        var padded = $"{{{name}:00}}";
        var unpadded = $"{{{name}}}";
        var paddedValue = value?.ToString("00") ?? string.Empty;
        var plainValue = value?.ToString() ?? string.Empty;
        return input
            .Replace(padded, paddedValue, StringComparison.OrdinalIgnoreCase)
            .Replace(unpadded, plainValue, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the template if it is valid for the kind, else the kind default (a bad stored template degrades).</summary>
    private static string ResolveTemplate(EntityKind kind, string? template) {
        var fallback = DefaultFor(kind) ?? throw new ArgumentException($"No media naming template for kind {kind}.", nameof(kind));
        return !string.IsNullOrWhiteSpace(template) && Validate(kind, template) is null ? template : fallback;
    }

    private static string FirstSegment(string template) => FirstSegment(template.AsSpan());

    private static string FirstSegment(ReadOnlySpan<char> template) {
        var slash = template.IndexOf('/');
        return slash < 0 ? template.ToString() : template[..slash].ToString();
    }

    private static string FolderOf(string relativePath) {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? string.Empty : relativePath[..slash];
    }
}
