using Prismedia.Application.Files;

namespace Prismedia.Application.Acquisition;

/// <summary>Episode coverage backed by nonempty files currently present on disk, rather than stale catalog links.</summary>
public static class TvOwnedEpisodeCoverage {
    /// <summary>Checks each distinct paired-episode file once and returns the season/episode positions it supplies.</summary>
    public static IReadOnlySet<(int Season, int Episode)> Read(TvSeriesDiskLayout? layout) {
        if (layout is null) return new HashSet<(int, int)>();
        var present = layout.Seasons.Values.SelectMany(season => season.EpisodeFileByNumber.Values)
            .Distinct(FileSystemPathComparison.Comparer).Where(IsPresent)
            .ToHashSet(FileSystemPathComparison.Comparer);
        return layout.Seasons.SelectMany(season => season.Value.EpisodeFileByNumber
            .Where(file => present.Contains(file.Value)).Select(file => (season.Key, file.Key))).ToHashSet();
    }

    private static bool IsPresent(string path) {
        try {
            var file = new FileInfo(path);
            return file.Exists && file.Length > 0;
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }
}
