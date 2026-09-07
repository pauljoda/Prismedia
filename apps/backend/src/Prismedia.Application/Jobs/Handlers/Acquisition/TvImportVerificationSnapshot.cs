using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Captures the catalog, ownership and filesystem facts an untouched TV plan depends on.</summary>
internal static class TvImportVerificationSnapshot {
    public static async Task<string> ReadAsync(ILibraryScanRootPersistence roots, IImportTargetIndex targets,
        AcquisitionImportContext import, TvImportCheckpoint checkpoint, CancellationToken token) {
        var root = await roots.GetLibraryRootAsync(checkpoint.LibraryRootId, token);
        var enabled = await roots.GetEnabledRootsAsync(token);
        var layout = import.EntityId is { } entityId ? await targets.GetTvLayoutAsync(entityId, token) : null;
        var catalog = import.EntityId is { } catalogId ? await targets.GetSeriesEpisodeCatalogAsync(catalogId, token) : [];
        // This is transient comparison evidence, not a persisted contract. Explicit ordering makes
        // a different database enumeration order equivalent while preserving every ambiguous owner.
        return JsonSerializer.Serialize(new {
            Root = root is null ? null : new { root.Id, root.Path, root.Enabled, root.ScanVideos },
            VideoRoots = enabled.Where(item => item.ScanVideos).OrderBy(item => item.Id).Select(item => new { item.Id, item.Path }),
            Layout = layout is null ? null : new {
                layout.SeriesEntityId, layout.SeriesFolderPath,
                Unresolved = layout.UnresolvedSourcePaths.Order(StringComparer.Ordinal),
                Seasons = layout.Seasons.OrderBy(pair => pair.Key).Select(pair => new {
                    Number = pair.Key, pair.Value.SeasonEntityId, pair.Value.FolderPath, pair.Value.HasUnresolvedOwnership,
                    Ambiguous = pair.Value.AmbiguousEpisodeNumbers.Order(),
                    Files = pair.Value.EpisodeFileByNumber.OrderBy(file => file.Key)
                })
            },
            Catalog = catalog.OrderBy(season => season.SeasonNumber).ThenBy(season => season.SeasonEntityId).Select(season => new {
                season.SeasonNumber, season.SeasonEntityId,
                Episodes = season.Episodes.OrderBy(episode => episode.Episode).ThenBy(episode => episode.EntityId)
            }),
            Files = checkpoint.Units.Select(unit => new {
                Source = Observe(unit.SourceAbsolutePath!),
                TargetExists = File.Exists(unit.TargetAbsolutePath) || Directory.Exists(unit.TargetAbsolutePath)
            })
        });
    }

    private static object Observe(string path) {
        var file = new FileInfo(path);
        return new { file.FullName, file.Exists, Length = file.Exists ? file.Length : (long?)null,
            Modified = file.Exists ? file.LastWriteTimeUtc : (DateTime?)null };
    }
}
