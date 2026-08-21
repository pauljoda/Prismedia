using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Routes observed filesystem paths to the scan kinds whose media families can own them, so one
/// changed video no longer queues gallery/audio/book scans of the same root. Extension sets
/// mirror each handler's snapshot categories: a path a kind would not record in its snapshot
/// cannot change that kind's delta, so routing it there would only produce a no-op job.
/// Directories (and deleted extensionless paths, which cannot be distinguished from removed
/// directories) route to every enabled kind because their former contents are unknown.
/// Recognized-but-disabled kinds and unrecognized file extensions route nowhere.
/// </summary>
public static class MediaScanKindRouter {
    /// <summary>Routes each path to the scan kinds that should observe it.</summary>
    /// <param name="selection">The media families enabled on the root.</param>
    /// <param name="absolutePaths">Observed absolute paths (files or directories).</param>
    /// <returns>Per scan kind, the paths it should record as change intents.</returns>
    public static IReadOnlyDictionary<JobType, IReadOnlyList<string>> Route(
        LibraryScanSelection selection,
        IReadOnlyCollection<string> absolutePaths) {
        var routed = new Dictionary<JobType, List<string>>();

        void Add(JobType type, string path) {
            if (!routed.TryGetValue(type, out var paths)) {
                paths = [];
                routed[type] = paths;
            }

            paths.Add(path);
        }

        void AddAllEnabled(string path) {
            foreach (var type in LibraryScanJobs.ScanJobTypesFor(selection)) {
                Add(type, path);
            }
        }

        foreach (var path in absolutePaths) {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension) || Directory.Exists(path)) {
                AddAllEnabled(path);
                continue;
            }

            if (selection.Videos &&
                (SupportedExtensions.Video.Contains(extension) ||
                 SupportedExtensions.VideoSubtitleSidecar.Contains(extension))) {
                Add(JobType.ScanLibrary, path);
            }

            if (selection.Images && SupportedExtensions.Image.Contains(extension)) {
                Add(JobType.ScanGallery, path);
            }

            if (selection.Audio && SupportedExtensions.Audio.Contains(extension)) {
                Add(JobType.ScanAudio, path);
            }

            if (selection.Books &&
                (SupportedExtensions.Book.Contains(extension) ||
                 SupportedExtensions.Audiobook.Contains(extension))) {
                Add(JobType.ScanBook, path);
            }

            if (selection.Comics &&
                (SupportedExtensions.ComicArchive.Contains(extension) ||
                 SupportedExtensions.ComicPage.Contains(extension) ||
                 SupportedExtensions.IsComicMetadataSidecar(path))) {
                Add(JobType.ScanComic, path);
            }
        }

        return routed.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value);
    }

    /// <summary>The scan selection that covers exactly the routed kinds.</summary>
    public static LibraryScanSelection SelectionFor(IReadOnlyDictionary<JobType, IReadOnlyList<string>> routed) =>
        new(
            Videos: routed.ContainsKey(JobType.ScanLibrary),
            Images: routed.ContainsKey(JobType.ScanGallery),
            Audio: routed.ContainsKey(JobType.ScanAudio),
            Books: routed.ContainsKey(JobType.ScanBook),
            Comics: routed.ContainsKey(JobType.ScanComic));
}
