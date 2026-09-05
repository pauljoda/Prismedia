using Prismedia.Application.Acquisition;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Shared selection for probing and replacing the same unambiguous video in an upgrade payload.</summary>
internal static class VideoUpgradeFileSelection {
    public static string? Find(string path) {
        if (File.Exists(path)) {
            return MovieImportPlanBuilder.VideoExtensions.Contains(Path.GetExtension(path)) ? path : null;
        }
        if (!Directory.Exists(path)) {
            return null;
        }
        var videos = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => MovieImportPlanBuilder.VideoExtensions.Contains(Path.GetExtension(file)))
            .ToArray();
        var primaries = videos.Where(file => !MovieImportPlanBuilder.IsSampleFile(file)).Take(2).ToArray();
        // Match movie import's sample-only fallback, but never guess between multiple features or
        // episode files by size. The inspector and replacer must choose the exact same source.
        return primaries.Length == 1 ? primaries[0]
            : primaries.Length == 0 && videos.Length == 1 ? videos[0]
            : null;
    }
}
