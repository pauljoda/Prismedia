using System.Text.RegularExpressions;
using Prismedia.Application.Files;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// One section (disc) of an album: a directory of tracks within the album. An album with no
/// discs has a single section whose <see cref="DirectoryPath"/> is the album folder and whose
/// <see cref="Label"/> is null. Disc subfolders add further sections whose label is the folder
/// name. Track numbering restarts per section.
/// </summary>
/// <param name="DirectoryPath">Directory that directly holds this section's audio files.</param>
/// <param name="Label">Section label (e.g. "Disc 1"), or null for the album's own tracks.</param>
/// <param name="Order">Zero-based ordinal of the section within the album.</param>
public sealed record AudioSection(string DirectoryPath, string? Label, int Order);

/// <summary>
/// An album folder resolved by the classifier, with its owning artist (if any) and ordered
/// sections.
/// </summary>
/// <param name="Path">Album folder path.</param>
/// <param name="Title">Album title (folder name).</param>
/// <param name="ArtistPath">Owning artist folder path, or null for an album with no artist folder.</param>
/// <param name="Sections">Ordered sections holding the album's tracks.</param>
public sealed record AudioAlbum(string Path, string Title, string? ArtistPath, IReadOnlyList<AudioSection> Sections);

/// <summary>An artist/band folder resolved by the classifier.</summary>
/// <param name="Path">Artist folder path.</param>
/// <param name="Title">Artist title (folder name).</param>
public sealed record AudioArtist(string Path, string Title);

/// <summary>Resolved music library layout: the artist groupings and albums discovered under a root.</summary>
/// <param name="Artists">Artist folders (immediate children of the root that group albums).</param>
/// <param name="Albums">Album folders, each optionally linked to an artist.</param>
public sealed record AudioLibraryLayout(IReadOnlyList<AudioArtist> Artists, IReadOnlyList<AudioAlbum> Albums);

/// <summary>
/// Classifies a music library's directory tree into the two supported on-disk layouts —
/// <c>Album/Songs</c> and <c>Artist/Album/Songs</c> — plus their multi-disc extensions, where a
/// disc subfolder inside an album becomes a <see cref="AudioSection"/> rather than a nested album.
///
/// The classifier works leaf-first from the directories that directly contain audio files:
/// <list type="bullet">
/// <item>A folder with direct tracks is an <b>Album</b> (its disc subfolders become sections).</item>
/// <item>A folder of only folders is an <b>Artist</b> grouping albums — unless its track-bearing
/// children are all disc-named, in which case it is an album with sections.</item>
/// </list>
/// The maximum nesting is Artist → Album → Section; anything deeper is flattened into sections so
/// the layout never chains arbitrarily.
/// </summary>
public static class AudioLibraryClassifier {
    private static readonly StringComparer PathComparer = FileSystemPathComparison.Comparer;
    private static readonly StringComparer DisplayComparer = StringComparer.OrdinalIgnoreCase;

    // Matches disc/section folder names: "Disc 1", "CD2", "Side A", "Vol. 3", "Part II",
    // "Disc One", etc. Used to tell a multi-disc album apart from an artist-of-albums.
    private static readonly Regex SectionFolderPattern = new(
        @"^(disc|disk|cd|side|vol|volume|part|disque)\s*[._\-]?\s*(\d+|[ivxlcdm]+|[a-d]|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Resolves the artist/album/section layout for a music root.
    /// </summary>
    /// <param name="rootPath">Library root path. Directly-contained audio files are loose tracks and are not returned here.</param>
    /// <param name="trackDirectories">Directories that directly contain audio files (e.g. scan directory groups), excluding the root.</param>
    /// <returns>The resolved artists and albums.</returns>
    public static AudioLibraryLayout Classify(string rootPath, IEnumerable<string> trackDirectories) {
        var root = Normalize(rootPath);
        var trackDirs = trackDirectories
            .Select(Normalize)
            .Where(dir => IsStrictDescendant(root, dir))
            .ToHashSet(PathComparer);

        // Group every track directory by the immediate child of the root on its path (its
        // depth-1 ancestor). Each such top folder is classified independently as artist or album.
        var byTopFolder = new Dictionary<string, List<string>>(PathComparer);
        foreach (var dir in trackDirs) {
            var top = AncestorAtDepth(root, dir, 1);
            if (top is null) {
                continue;
            }

            if (!byTopFolder.TryGetValue(top, out var list)) {
                byTopFolder[top] = list = [];
            }

            list.Add(dir);
        }

        var artists = new List<AudioArtist>();
        var albums = new List<AudioAlbum>();

        foreach (var (topFolder, dirsUnder) in byTopFolder) {
            var hasDirectTracks = trackDirs.Contains(topFolder);
            var deeperExist = dirsUnder.Any(dir => Depth(root, dir) >= 3);
            var depth2Children = dirsUnder
                .Where(dir => trackDirs.Contains(dir) && SamePath(Parent(dir), topFolder))
                .ToList();

            var isArtist =
                !hasDirectTracks &&
                (deeperExist ||
                 depth2Children.Count == 0 ||
                 !depth2Children.All(dir => IsSectionFolderName(FolderName(dir))));

            if (!isArtist) {
                // Album with no artist folder: itself plus any disc subfolders as sections.
                albums.Add(BuildAlbum(root, topFolder, artistPath: null, trackDirs));
                continue;
            }

            artists.Add(new AudioArtist(topFolder, FolderName(topFolder)));

            // Each distinct depth-2 ancestor-or-self of a track directory under this artist is an album.
            var albumDirs = dirsUnder
                .Select(dir => AncestorAtDepth(root, dir, 2))
                .Where(dir => dir is not null)
                .Select(dir => dir!)
                .Distinct(PathComparer);
            foreach (var albumDir in albumDirs) {
                albums.Add(BuildAlbum(root, albumDir, artistPath: topFolder, trackDirs));
            }
        }

        return new AudioLibraryLayout(
            artists.OrderBy(artist => FolderName(artist.Path), DisplayComparer).ToList(),
            albums
                .OrderBy(album => album.ArtistPath ?? string.Empty, DisplayComparer)
                .ThenBy(album => FolderName(album.Path), DisplayComparer)
                .ToList());
    }

    /// <summary>Whether a folder name looks like an album disc/section (e.g. "Disc 1", "CD2", "Side A").</summary>
    public static bool IsSectionFolderName(string folderName) =>
        !string.IsNullOrWhiteSpace(folderName) && SectionFolderPattern.IsMatch(folderName.Trim());

    private static AudioAlbum BuildAlbum(string root, string albumPath, string? artistPath, HashSet<string> trackDirs) {
        var sections = new List<AudioSection>();
        var order = 0;

        if (trackDirs.Contains(albumPath)) {
            sections.Add(new AudioSection(albumPath, Label: null, Order: order++));
        }

        var discDirs = trackDirs
            .Where(dir => IsStrictDescendant(albumPath, dir))
            .OrderBy(dir => Depth(root, dir))
            .ThenBy(FolderName, DisplayComparer);
        foreach (var discDir in discDirs) {
            sections.Add(new AudioSection(discDir, FolderName(discDir), order++));
        }

        return new AudioAlbum(albumPath, FolderName(albumPath), artistPath, sections);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null && FileSystemPathComparison.Equals(left, right);

    private static string FolderName(string path) => Path.GetFileName(path);

    private static string? Parent(string path) {
        var parent = Path.GetDirectoryName(path);
        return parent is null ? null : Normalize(parent);
    }

    private static string[] RelativeSegments(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    private static int Depth(string root, string path) {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ? 0 : RelativeSegments(root, path).Length;
    }

    private static bool IsStrictDescendant(string ancestor, string path) {
        if (SamePath(ancestor, path)) {
            return false;
        }

        var relative = Path.GetRelativePath(ancestor, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>Returns the ancestor of <paramref name="path"/> exactly <paramref name="depth"/> levels below the root, or null when the path is shallower than that.</summary>
    private static string? AncestorAtDepth(string root, string path, int depth) {
        var segments = RelativeSegments(root, path);
        if (segments.Length < depth) {
            return null;
        }

        return Normalize(Path.Combine([root, .. segments[..depth]]));
    }
}
