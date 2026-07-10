using Microsoft.Extensions.Logging;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>Best-effort execution boundary for post-persistence import housekeeping.</summary>
internal static class ImportedMaterializationHousekeeping {
    public static async Task TryAsync(
        ILogger logger,
        string failureMessage,
        Func<Task> action) {
        try {
            await action();
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "{FailureMessage}", failureMessage);
        }
    }
}

/// <summary>
/// The exact files placed by one acquisition that must become Entity-owned before the acquisition
/// may report <see cref="AcquisitionStatus.Imported"/>.
/// </summary>
/// <param name="AcquisitionId">Acquisition whose import hint owns the binding.</param>
/// <param name="EntityId">Stable requested Entity, when the acquisition has one.</param>
/// <param name="Root">Library root that owns the placed files.</param>
/// <param name="PlacedMediaPaths">Exact media files placed by the import; non-media sidecars are excluded.</param>
public sealed record ImportedEntityMaterializationRequest(
    Guid AcquisitionId,
    Guid? EntityId,
    LibraryRootData Root,
    IReadOnlyList<string> PlacedMediaPaths);

/// <summary>
/// One registered media-kind policy for synchronously applying the same binding and persistence
/// semantics as that kind's scanner, scoped strictly to one import's placed files.
/// </summary>
public interface IImportedEntityMaterializationPolicy {
    /// <summary>The acquisition kind handled by this policy.</summary>
    EntityKind Kind { get; }

    /// <summary>Materializes the request's exact files without reconciling unrelated library content.</summary>
    Task MaterializeAsync(
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dispatches imported files to their registered kind policy and enforces the shared ready-state
/// postcondition before an import engine can mark its acquisition Imported.
/// </summary>
public interface IImportedEntityMaterializer {
    /// <summary>Materializes and verifies the exact import output.</summary>
    Task MaterializeAsync(
        EntityKind kind,
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Registered, branch-free imported Entity materialization dispatcher.</summary>
public sealed class ImportedEntityMaterializer(
    IEnumerable<IImportedEntityMaterializationPolicy> policies,
    IImportedEntityReadinessPersistence readiness) : IImportedEntityMaterializer {
    private readonly IReadOnlyDictionary<EntityKind, IImportedEntityMaterializationPolicy> _byKind =
        policies.ToDictionary(policy => policy.Kind);

    public async Task MaterializeAsync(
        EntityKind kind,
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken) {
        if (!_byKind.TryGetValue(kind, out var policy)) {
            throw new InvalidOperationException($"No imported Entity materializer is registered for {kind.ToCode()}.");
        }

        var normalized = NormalizeAndValidate(request.Root, request.PlacedMediaPaths);
        var normalizedRequest = request with { PlacedMediaPaths = normalized };
        await policy.MaterializeAsync(context, normalizedRequest, cancellationToken);

        if (!await readiness.IsReadyAsync(
                request.EntityId,
                normalized,
                cancellationToken)) {
            throw new InvalidOperationException(
                $"The {kind.ToCode()} import was placed on disk but its Entity graph is not source-backed and ready.");
        }
    }

    private static IReadOnlyList<string> NormalizeAndValidate(
        LibraryRootData root,
        IReadOnlyList<string> placedMediaPaths) {
        if (placedMediaPaths.Count == 0) {
            throw new InvalidOperationException("An import cannot be materialized without placed media files.");
        }

        var rootPath = Path.GetFullPath(root.Path);
        var normalizedRoot = Path.EndsInDirectorySeparator(rootPath)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        var normalized = placedMediaPaths
            .Select(Path.GetFullPath)
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        foreach (var path in normalized) {
            if (!path.StartsWith(normalizedRoot, FileSystemPathComparison.Comparison) || !File.Exists(path)) {
                throw new InvalidOperationException($"An imported media path is missing or outside its library root: {path}");
            }
        }

        return normalized;
    }
}

/// <summary>Book import policy backed by the book scanner's exact-path materialization seam.</summary>
public sealed class ImportedBookMaterializationPolicy(ScanBookJobHandler scan)
    : IImportedEntityMaterializationPolicy {
    public EntityKind Kind => EntityKind.Book;

    public Task MaterializeAsync(
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken) =>
        scan.MaterializeImportedPathsAsync(
            context, request.AcquisitionId, request.Root, request.PlacedMediaPaths, cancellationToken);
}

/// <summary>Movie import policy backed by the video scanner's exact-path materialization seam.</summary>
public sealed class ImportedMovieMaterializationPolicy(ScanLibraryJobHandler scan)
    : IImportedEntityMaterializationPolicy {
    public EntityKind Kind => EntityKind.Movie;

    public Task MaterializeAsync(
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken) =>
        scan.MaterializeImportedPathsAsync(
            context, request.AcquisitionId, request.Root, request.PlacedMediaPaths, cancellationToken);
}

/// <summary>Album import policy backed by the audio scanner's exact-path materialization seam.</summary>
public sealed class ImportedAlbumMaterializationPolicy(ScanAudioJobHandler scan)
    : IImportedEntityMaterializationPolicy {
    public EntityKind Kind => EntityKind.AudioLibrary;

    public Task MaterializeAsync(
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken) =>
        scan.MaterializeImportedPathsAsync(
            context, request.AcquisitionId, request.Root, request.PlacedMediaPaths, cancellationToken);
}
