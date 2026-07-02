using System.Text.RegularExpressions;

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
/// Pure movie import planning: picks the single primary video file out of a downloaded release and
/// renders its target path as <c>{Title (Year)}/{Title (Year)}.{ext}</c> under the library root. Sample
/// files are skipped, and a release carrying more than one full-size video (a multi-movie pack) blocks
/// for manual import rather than guessing. Extras/subtitle sidecars are intentionally left behind in
/// v1 — the scan derives everything from the primary video.
/// </summary>
public static partial class MovieImportPlanBuilder {
    /// <summary>
    /// Video extensions the movie importer accepts. Mirrors scan discovery's video set — what the
    /// importer places, the scanner must pick up.
    /// </summary>
    private static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".mp4", ".m4v", ".mkv", ".mov", ".webm", ".avi", ".wmv", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg"
    };

    [GeneratedRegex(@"(?:^|[\s._\-\[(])sample(?:$|[\s._\-\])])", RegexOptions.IgnoreCase)]
    private static partial Regex SampleTokenRegex();

    /// <summary>Plans the import of a downloaded movie release given the acquisition's title/year metadata.</summary>
    public static ImportPlan Plan(IReadOnlyList<ImportCandidateFile> files, ImportTemplateContext context) {
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

        var folder = MovieFolderName(context);
        var extension = Path.GetExtension(primary.RelativePath);
        return ImportPlan.For([new ImportPlanItem(primary.RelativePath, $"{folder}/{folder}{extension}")]);
    }

    /// <summary>The sanitized movie folder (and file base) name: <c>Title (Year)</c>, or just the title when the year is unknown.</summary>
    public static string MovieFolderName(ImportTemplateContext context) =>
        ImportPlanBuilder.SanitizeSegment(context.Year is { } year ? $"{context.Title} ({year})" : context.Title);
}

/// <summary>
/// Pure music import planning: places every audio file of a downloaded album release under
/// <c>{Artist}/{Album}/</c>, preserving inner structure (disc folders) after stripping the release's
/// single wrapper folder, plus any cover-art images flattened into the album folder. No ambiguity
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

    /// <summary>Plans the import of a downloaded album release into <c>{artist}/{album}/</c>.</summary>
    public static ImportPlan Plan(IReadOnlyList<ImportCandidateFile> files, string artist, string album) {
        var audio = files
            .Where(file => AudioExtensions.Contains(Path.GetExtension(file.RelativePath)))
            .ToArray();
        if (audio.Length == 0) {
            return ImportPlan.Block(ImportBlockReason.NoSupportedPayload);
        }

        var folder = AlbumFolderRelative(artist, album);
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

    /// <summary>The sanitized <c>{artist}/{album}</c> folder path relative to the library root.</summary>
    public static string AlbumFolderRelative(string artist, string album) =>
        $"{ImportPlanBuilder.SanitizeSegment(artist)}/{ImportPlanBuilder.SanitizeSegment(album)}";

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
                && string.Equals(first[shared], segments[shared], StringComparison.OrdinalIgnoreCase)) {
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
        return candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }
}
