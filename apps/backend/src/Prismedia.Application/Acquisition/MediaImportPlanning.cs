using System.Text.RegularExpressions;
using Prismedia.Application.Files;

namespace Prismedia.Application.Acquisition;

/// <summary>A downloaded file offered to an import planner: its path relative to the download content root, and its size.</summary>
public sealed record ImportCandidateFile(string RelativePath, long SizeBytes);

/// <summary>
/// Reads a completed download's payload for import planning: the files (relative path + size) and the
/// content root the paths are relative to. A single-file download's root is its parent directory.
/// </summary>
public interface IDownloadPayloadReader {
    /// <summary>The payload at <paramref name="contentPath"/>, or null when the path no longer exists.</summary>
    DownloadPayload? Read(string contentPath);
}

/// <summary>The files of a completed download, relative to <paramref name="ContentRoot"/>.</summary>
public sealed record DownloadPayload(string ContentRoot, IReadOnlyList<ImportCandidateFile> Files);

/// <summary>
/// One placeable TV file from a release: its payload-relative source, its decoded season/episode, and
/// the template's full three-segment render for it. <see cref="FileName"/> is the render's file segment,
/// reused when a merged import re-anchors the folders onto an existing series tree.
/// </summary>
public sealed record TvPlanUnit(string SourceRelativePath, int Season, int Episode, string TargetRelativePath) {
    /// <summary>The template-rendered file name (the render's last segment).</summary>
    public string FileName => TargetRelativePath.Split('/')[^1];

    /// <summary>
    /// Episodes this file COVERS beyond <see cref="Episode"/> (title-aligned multi-episode files: a
    /// single file whose name carries two provider episode titles satisfies both numbers). Empty for
    /// ordinary single-episode files. The import binds these to the same placed file so the extra
    /// episodes play the shared file instead of staying wanted forever.
    /// </summary>
    public IReadOnlyList<int> ExtraEpisodes { get; init; } = [];
}

/// <summary>One provider episode of the season being imported: its number and title, for title-based file alignment.</summary>
public sealed record TvEpisodeTitle(int Episode, string Title);

/// <summary>The unit-level TV plan: either blocked (same reasons as <see cref="ImportPlan"/>) or the placeable units.</summary>
public sealed record TvUnitsPlan(bool Blocked, ImportBlockReason? BlockReason, IReadOnlyList<TvPlanUnit> Units) {
    public static TvUnitsPlan Block(ImportBlockReason reason) => new(true, reason, []);
    public static TvUnitsPlan For(IReadOnlyList<TvPlanUnit> units) => new(false, null, units);
}

/// <summary>
/// Pure movie import planning: picks the single primary video file out of a downloaded release and
/// renders its target path from the profile's naming template (default
/// <c>{Title} ({Year})/{Title} ({Year}).{ext}</c>) under the library root. Sample files are skipped, and
/// a release carrying more than one full-size video (a multi-movie pack) blocks for manual import rather
/// than guessing. Extras/subtitle sidecars are intentionally left behind in v1 — the scan derives
/// everything from the primary video.
/// </summary>
public static partial class MovieImportPlanBuilder {
    /// <summary>
    /// Video extensions the movie importer accepts. Mirrors scan discovery's video set — what the
    /// importer places, the scanner must pick up. The single source of truth for the video file set;
    /// the owned-file replacer references it so an upgrade swap finds the same files the importer placed.
    /// </summary>
    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".mp4", ".m4v", ".mkv", ".mov", ".webm", ".avi", ".wmv", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg"
    };

    [GeneratedRegex(@"(?:^|[\s._\-\[(])sample(?:$|[\s._\-\])])", RegexOptions.IgnoreCase)]
    private static partial Regex SampleTokenRegex();

    /// <summary>
    /// Plans the import of a downloaded movie release given the acquisition's metadata and the profile's
    /// naming template (<paramref name="template"/> defaults to <see cref="MediaNamingTemplates.MovieDefault"/>;
    /// a blank or invalid template degrades to the default). <paramref name="quality"/> is the detected
    /// quality code of the selected release for the optional <c>{Quality}</c> token.
    /// </summary>
    public static ImportPlan Plan(
        IReadOnlyList<ImportCandidateFile> files,
        ImportTemplateContext context,
        string? template = null,
        string? quality = null) {
        var videos = files
            .Where(file => VideoExtensions.Contains(Path.GetExtension(file.RelativePath)))
            .ToArray();
        if (videos.Length == 0) {
            return ImportPlan.Block(ImportBlockReason.NoSupportedPayload);
        }

        // Samples are decoys, not features — but if the release contains ONLY sample-named videos,
        // trust the payload over the naming.
        var candidates = videos
            .Where(file => !SampleTokenRegex().IsMatch(Path.GetFileNameWithoutExtension(file.RelativePath)))
            .ToArray();
        if (candidates.Length == 0) {
            candidates = videos;
        }

        var primary = candidates.MaxBy(file => file.SizeBytes)!;
        // A second video at half the primary's size or more reads as another feature (a multi-movie
        // pack), not an extra — block for manual import instead of picking one.
        if (candidates.Any(file => file != primary && file.SizeBytes * 2 >= primary.SizeBytes)) {
            return ImportPlan.Block(ImportBlockReason.AmbiguousMultiplePrimaries);
        }

        var naming = NamingContext(context, quality, Path.GetExtension(primary.RelativePath));
        var target = MediaNamingTemplates.RenderMoviePath(template, naming);
        return ImportPlan.For([new ImportPlanItem(primary.RelativePath, target)]);
    }

    /// <summary>The movie folder (and scan-hint folder) the template renders — derived from the SAME render as placement.</summary>
    public static string MovieFolderRelative(ImportTemplateContext context, string? template = null, string? quality = null) =>
        MediaNamingTemplates.RenderMovieFolder(template, NamingContext(context, quality, extension: string.Empty));

    private static MediaNamingContext NamingContext(ImportTemplateContext context, string? quality, string extension) =>
        new(context.Title, Year: context.Year, Quality: quality, Extension: extension);
}

/// <summary>
/// Pure music import planning: places every audio file of a downloaded album release under the album
/// folder the profile's naming template renders (default <c>{Artist}/{Album}</c>), preserving inner
/// structure (disc folders) after stripping the release's single wrapper folder, plus any cover-art
/// images flattened into the album folder. Only the album FOLDER is templated — track files keep their
/// release names and inner disc structure, so track renaming is intentionally out of scope. No ambiguity
/// blocks — an album release maps wholesale.
/// </summary>
public static class MusicImportPlanBuilder {
    /// <summary>Audio extensions the music importer accepts. Mirrors scan discovery's audio set.</summary>
    private static readonly IReadOnlySet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma", ".opus",
        ".aiff", ".aif", ".alac", ".ape", ".dsf", ".dff", ".wv"
    };

    /// <summary>Cover-art extensions carried alongside the audio so the album folder keeps its artwork.</summary>
    private static readonly IReadOnlySet<string> ArtExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    /// <summary>Whether a planned payload path is source media rather than carried cover art.</summary>
    public static bool IsAudioFile(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Plans the import of a downloaded album release into the album folder the naming template renders
    /// (<paramref name="template"/> defaults to <see cref="MediaNamingTemplates.MusicDefault"/>; a blank or
    /// invalid template degrades to the default).
    /// </summary>
    public static ImportPlan Plan(IReadOnlyList<ImportCandidateFile> files, string artist, string album, string? template = null, int? year = null) {
        var audio = files
            .Where(file => AudioExtensions.Contains(Path.GetExtension(file.RelativePath)))
            .ToArray();
        if (audio.Length == 0) {
            return ImportPlan.Block(ImportBlockReason.NoSupportedPayload);
        }

        var folder = AlbumFolderRelative(artist, album, template, year);
        var prefix = CommonDirectoryPrefix(audio.Select(file => file.RelativePath).ToArray());
        var items = new List<ImportPlanItem>(files.Count);
        foreach (var file in audio.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)) {
            items.Add(new ImportPlanItem(file.RelativePath, $"{folder}/{SanitizeRelative(StripPrefix(file.RelativePath, prefix))}"));
        }

        foreach (var art in files.Where(file => ArtExtensions.Contains(Path.GetExtension(file.RelativePath)))
                     .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)) {
            items.Add(new ImportPlanItem(
                art.RelativePath,
                $"{folder}/{ImportPlanBuilder.SanitizeSegment(Path.GetFileName(art.RelativePath))}"));
        }

        return ImportPlan.For(items);
    }

    /// <summary>
    /// The album folder path (relative to the library root) the template renders — the SAME render used
    /// for placement, so the scan hint keyed on this folder matches where tracks were placed.
    /// </summary>
    public static string AlbumFolderRelative(string artist, string album, string? template = null, int? year = null) =>
        MediaNamingTemplates.RenderMusicAlbumFolder(template, new MediaNamingContext(album, Artist: artist, Album: album, Year: year));

    /// <summary>Sanitizes every segment of a relative path, keeping its directory structure.</summary>
    private static string SanitizeRelative(string relativePath) =>
        string.Join('/', relativePath
            .Split('/', '\\', StringSplitOptions.RemoveEmptyEntries)
            .Select(ImportPlanBuilder.SanitizeSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment)));

    /// <summary>
    /// The directory segments every path shares (e.g. the release's single wrapper folder), so the
    /// album folder holds tracks/discs directly instead of nesting the release folder name.
    /// </summary>
    private static string[] CommonDirectoryPrefix(IReadOnlyList<string> paths) {
        var first = SplitDirectories(paths[0]);
        var length = first.Length;
        foreach (var path in paths.Skip(1)) {
            var segments = SplitDirectories(path);
            var shared = 0;
            while (shared < length && shared < segments.Length
                && string.Equals(first[shared], segments[shared], FileSystemPathComparison.Comparison)) {
                shared++;
            }

            length = shared;
            if (length == 0) {
                break;
            }
        }

        return first[..length];
    }

    private static string[] SplitDirectories(string relativePath) {
        var segments = relativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= 1 ? [] : segments[..^1];
    }

    private static string StripPrefix(string relativePath, string[] prefix) {
        var segments = relativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', segments[prefix.Length..]);
    }
}

/// <summary>
/// Pure TV import planning: places a release's episode files under the layout the profile's naming
/// template renders (default <c>{Series}/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}</c>),
/// exactly the three-segment series/season/episode layout the video scan materializes a series hierarchy
/// from. Episode identity comes from the SxxEyy / 1x05 tokens in each file name (via
/// <see cref="TvReleaseTokens"/> — the same decode the decision engine uses); a single-episode
/// acquisition whose file carries no token falls back to the unit stamped on the acquisition. Sample
/// files are skipped, and a season pack whose files carry no tokens at all blocks for manual import
/// rather than guessing episode order.
/// </summary>
public static partial class TvImportPlanBuilder {
    /// <summary>Video extensions the TV importer accepts. Mirrors scan discovery's video set.</summary>
    private static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".mp4", ".m4v", ".mkv", ".mov", ".webm", ".avi", ".wmv", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg"
    };

    [GeneratedRegex(@"(?:^|[\s._\-\[(])sample(?:$|[\s._\-\])])", RegexOptions.IgnoreCase)]
    private static partial Regex SampleTokenRegex();

    /// <summary>
    /// Plans the import of a downloaded TV release. <paramref name="series"/> names the series folder;
    /// <paramref name="seasonNumber"/>/<paramref name="episodeNumber"/> are the acquisition's unit,
    /// used when a file names no unit of its own. <paramref name="template"/> defaults to
    /// <see cref="MediaNamingTemplates.TvDefault"/> (blank/invalid degrades to it); <paramref name="quality"/>
    /// is the detected quality code for the optional <c>{Quality}</c> token.
    /// </summary>
    public static ImportPlan Plan(
        IReadOnlyList<ImportCandidateFile> files,
        string series,
        int? seasonNumber,
        int? episodeNumber,
        string? template = null,
        string? quality = null,
        IReadOnlyList<TvEpisodeTitle>? episodeTitles = null) {
        var units = PlanUnits(files, series, seasonNumber, episodeNumber, template, quality, episodeTitles);
        if (units.Blocked) {
            return ImportPlan.Block(units.BlockReason!.Value);
        }

        return ImportPlan.For(units.Units
            .Select(unit => new ImportPlanItem(unit.SourceRelativePath, unit.TargetRelativePath))
            .ToArray());
    }

    /// <summary>
    /// The unit pass behind <see cref="Plan"/>: the same filtering, token decode, and template render,
    /// but returning each file's parsed season/episode alongside its render — so a merged import into
    /// an existing series folder can re-anchor the folders while keeping the template's file naming.
    /// </summary>
    public static TvUnitsPlan PlanUnits(
        IReadOnlyList<ImportCandidateFile> files,
        string series,
        int? seasonNumber,
        int? episodeNumber,
        string? template = null,
        string? quality = null,
        IReadOnlyList<TvEpisodeTitle>? episodeTitles = null) {
        var videos = files
            .Where(file => VideoExtensions.Contains(Path.GetExtension(file.RelativePath)))
            .Where(file => !SampleTokenRegex().IsMatch(Path.GetFileNameWithoutExtension(file.RelativePath)))
            .ToArray();
        if (videos.Length == 0) {
            return TvUnitsPlan.Block(ImportBlockReason.NoSupportedPayload);
        }

        var units = new List<TvPlanUnit>(videos.Length);
        foreach (var video in videos.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)) {
            var unit = TvReleaseTokens.ParseEpisode(Path.GetFileNameWithoutExtension(video.RelativePath));
            // A tokenless file is only placeable when the acquisition itself IS one episode.
            if (unit is null && (videos.Length > 1 || seasonNumber is null || episodeNumber is null)) {
                continue;
            }

            var (season, episode) = unit ?? (seasonNumber!.Value, episodeNumber!.Value);
            var naming = NamingContext(series, season, episode, quality, Path.GetExtension(video.RelativePath));
            units.Add(new TvPlanUnit(
                video.RelativePath, season, episode, MediaNamingTemplates.RenderTvPath(template, naming)));
        }

        // No file declared a placeable unit — importing by guesswork would scatter episodes; stop for
        // a human instead.
        if (units.Count == 0) {
            return TvUnitsPlan.Block(ImportBlockReason.AmbiguousMultiplePrimaries);
        }

        if (episodeTitles is { Count: > 0 }) {
            units = RealignByEpisodeTitles(
                units,
                episodeTitles,
                series,
                template,
                quality,
                evidenceSeason: seasonNumber);
        }

        // One structural episode slot has one playable owner. Numeric duplicates and title-aligned
        // covered episodes are both claims: importing either overlap under a collision suffix would let
        // the housekeeping scan mint duplicate episode Entities for the same season/position.
        var claimedSlots = units
            .SelectMany(unit => unit.ExtraEpisodes
                .Prepend(unit.Episode)
                .Select(episode => (unit.Season, Episode: episode)))
            .ToArray();
        if (claimedSlots.Distinct().Count() != claimedSlots.Length) {
            return TvUnitsPlan.Block(ImportBlockReason.AmbiguousMultiplePrimaries);
        }

        return TvUnitsPlan.For(units);
    }

    /// <summary>
    /// Realigns file episode numbers by the episode TITLES their names carry, for releases whose
    /// numbering diverges from the provider's ("as aired" packs bundle two provider episodes per file:
    /// the file labeled S01E01 carries the titles of provider episodes 1 AND 2, S01E02 carries 3+4 —
    /// numeric placement would import the wrong content into almost every episode slot). A file whose
    /// name-tail matches one or more provider titles is re-anchored at the LOWEST matched number and
    /// records the rest as covered extras (the import binds them to the same placed file). Files whose
    /// tails match nothing keep their numeric placement, and if the realignment would land two files on
    /// the same episode, the whole payload keeps its numeric numbers — never guess against a conflict.
    /// </summary>
    private static List<TvPlanUnit> RealignByEpisodeTitles(
        List<TvPlanUnit> units,
        IReadOnlyList<TvEpisodeTitle> episodeTitles,
        string series,
        string? template,
        string? quality,
        int? evidenceSeason) {
        var realigned = new List<TvPlanUnit>(units.Count);
        var changed = false;
        foreach (var unit in units) {
            if (evidenceSeason is { } requestedSeason && unit.Season != requestedSeason) {
                realigned.Add(unit);
                continue;
            }

            var tail = TvReleaseTokens.EpisodeTitleTail(Path.GetFileNameWithoutExtension(unit.SourceRelativePath));
            var matched = tail is null
                ? []
                : episodeTitles
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Title) &&
                        ReleaseTitleIdentity.ContainsRun(tail, candidate.Title))
                    .Select(candidate => candidate.Episode)
                    .Distinct()
                    .OrderBy(episode => episode)
                    .ToArray();
            if (matched.Length == 0) {
                realigned.Add(unit);
                continue;
            }

            var primary = matched[0];
            var extras = matched.Skip(1).ToArray();
            if (primary != unit.Episode || extras.Length > 0) {
                changed = true;
            }

            var naming = NamingContext(series, unit.Season, primary, quality, Path.GetExtension(unit.SourceRelativePath));
            realigned.Add(unit with {
                Episode = primary,
                TargetRelativePath = MediaNamingTemplates.RenderTvPath(template, naming),
                ExtraEpisodes = extras,
            });
        }

        if (!changed) {
            return units;
        }

        // A conflict (two files claiming one slot) means the title evidence is unreliable here —
        // keep the numeric truth for the whole payload rather than half-applying a guess.
        var slots = realigned.Select(unit => (unit.Season, unit.Episode)).ToArray();
        return slots.Distinct().Count() == slots.Length ? realigned : units;
    }

    /// <summary>
    /// The season FOLDER NAME (single segment) the template renders for <paramref name="season"/> —
    /// used to create a missing season folder inside an existing series folder with the user's naming.
    /// </summary>
    public static string SeasonFolderSegment(string series, int season, string? template = null) {
        var context = NamingContext(series, season, episode: null, quality: null, extension: string.Empty);
        var seasonFolder = MediaNamingTemplates.RenderTvSeasonFolder(template, context);
        return seasonFolder.Split('/')[^1];
    }

    /// <summary>
    /// The series folder (relative to the library root) a plan places into — the template's first segment,
    /// derived from the SAME render as placement so the scan's series bind matches.
    /// </summary>
    public static string SeriesFolderRelative(string series, string? template = null) =>
        MediaNamingTemplates.RenderTvSeriesFolder(template, NamingContext(series, season: null, episode: null, quality: null, extension: string.Empty));

    private static MediaNamingContext NamingContext(string series, int? season, int? episode, string? quality, string extension) =>
        new(series, Series: series, Season: season, Episode: episode, Quality: quality, Extension: extension);
}

/// <summary>
/// Resolves an <see cref="ImportPlan"/>'s relative moves to absolute paths under the library root,
/// refusing any target that escapes it — the shared final step for every per-kind import engine.
/// </summary>
public static class ImportTargetResolver {
    public static ResolvedImportPlan Resolve(string contentRoot, string libraryRootPath, ImportPlan plan) {
        if (plan.Blocked) {
            return new ResolvedImportPlan(true, plan.BlockReason, []);
        }

        var rootFull = Path.GetFullPath(libraryRootPath);
        var items = new List<ResolvedImportItem>(plan.Items.Count);
        foreach (var item in plan.Items) {
            var sourceAbsolute = Path.GetFullPath(Path.Combine(contentRoot, item.SourceRelativePath));
            var targetAbsolute = Path.GetFullPath(Path.Combine(rootFull, item.TargetRelativePath));
            if (!IsUnderRoot(targetAbsolute, rootFull)) {
                return ResolvedImportPlan.Block(ImportBlockReason.NoSupportedPayload);
            }

            items.Add(new ResolvedImportItem(sourceAbsolute, targetAbsolute));
        }

        return new ResolvedImportPlan(false, null, items);
    }

    private static bool IsUnderRoot(string candidate, string root) {
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, FileSystemPathComparison.Comparison);
    }
}
