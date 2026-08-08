using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
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
/// <param name="RequestedAudioTrackIdsByPath">Exact music planner decisions keyed by final source path.</param>
public sealed record ImportedEntityMaterializationRequest(
    Guid AcquisitionId,
    Guid? EntityId,
    LibraryRootData Root,
    IReadOnlyList<string> PlacedMediaPaths,
    IReadOnlyList<string>? ReplacedSourcePaths = null,
    IReadOnlyList<string>? RemovedSourcePaths = null,
    IReadOnlyDictionary<string, Guid>? RequestedAudioTrackIdsByPath = null);

/// <summary>One canonical Entity materialized from exact acquisition output.</summary>
public sealed record ImportedEntityReference(Guid Id, EntityKind Kind);

/// <summary>
/// Exact durable scope produced by acquisition materialization. Import engines consume this result
/// instead of choosing a scan kind or rediscovering a library root.
/// </summary>
public sealed record ImportedEntityMaterializationResult(
    IReadOnlyList<ImportedEntityReference> Entities,
    IReadOnlyList<Guid> TouchedAncestorIds,
    IReadOnlyList<string> AddedSourcePaths,
    IReadOnlyList<string> ReplacedSourcePaths,
    IReadOnlyList<string> RemovedSourcePaths,
    IReadOnlyList<ImportedEntityReference>? ReconciliationRoots = null) {
    /// <summary>
    /// Structural roots whose full trees should be processed after this import. Falling back to exact
    /// source owners preserves compatibility for materializers that cannot resolve a container root.
    /// </summary>
    public IReadOnlyList<ImportedEntityReference> ProcessingRoots =>
        ReconciliationRoots is { Count: > 0 } ? ReconciliationRoots : Entities;
}

/// <summary>Appends exact entity reconciliation nodes without exposing scan selection to import engines.</summary>
public static class ImportedEntityReconciliation {
    /// <summary>
    /// Queues one graph-local reconciliation per distinct structural processing root. An album or series
    /// therefore probes all imported leaves in one tree and performs one cascading identify afterward.
    /// </summary>
    public static async Task EnqueueAsync(
        JobContext context,
        ImportedEntityMaterializationResult result,
        AcquisitionFinalizeJobPayload? finalization,
        CancellationToken cancellationToken) {
        if (result.Entities.Count == 0) {
            throw new InvalidOperationException("Import materialization did not resolve an exact Entity reconciliation scope.");
        }

        var roots = result.ProcessingRoots
            .DistinctBy(entity => entity.Id)
            .ToArray();
        var scopedFinalization = finalization is null
            ? null
            : finalization with {
                ImportedEntityIds = result.Entities
                    .Select(entity => entity.Id)
                    .Distinct()
                    .ToArray()
            };
        for (var index = 0; index < roots.Length; index++) {
            var entity = roots[index];
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.ReconcileEntity,
                    entity.Kind,
                    entity.Id.ToString(),
                    label: null,
                    payloadJson: index == roots.Length - 1 ? scopedFinalization?.ToJson() : null),
                cancellationToken);
        }
    }
}

/// <summary>Applies an import's exact path mutations to one incremental scan snapshot.</summary>
public static class ImportedScanSnapshot {
    /// <summary>Inserts or updates placed files and removes only explicitly retired paths.</summary>
    public static async Task ApplyAsync(
        IScanSnapshotStore snapshots,
        Guid rootId,
        JobType scanJobType,
        IReadOnlyList<string> placedPaths,
        IReadOnlyList<string> removedPaths,
        CancellationToken cancellationToken) {
        var scanKind = scanJobType.ToCode();
        var previous = await snapshots.LoadAsync(rootId, scanKind, cancellationToken);
        var previousByPath = previous.ToDictionary(item => item.Path, FileSystemPathComparison.Comparer);
        var added = new List<FileSignature>();
        var changed = new List<FileSignature>();
        foreach (var path in placedPaths.Select(Path.GetFullPath)) {
            var file = new FileInfo(path);
            var signature = new FileSignature(path, file.Length, file.LastWriteTimeUtc.Ticks);
            if (!previousByPath.TryGetValue(path, out var old)) {
                added.Add(signature);
            } else if (old.SizeBytes != signature.SizeBytes || old.ModifiedTicks != signature.ModifiedTicks) {
                changed.Add(signature);
            }
        }

        var placed = placedPaths.Select(Path.GetFullPath).ToHashSet(FileSystemPathComparison.Comparer);
        var removed = removedPaths
            .Select(Path.GetFullPath)
            .Where(path => !placed.Contains(path) && previousByPath.ContainsKey(path))
            .Select(path => previousByPath[path])
            .ToArray();
        await snapshots.ApplyAsync(
            rootId,
            scanKind,
            new ScanDelta(added, removed, changed, UnchangedCount: 0),
            cancellationToken);
    }
}

/// <summary>
/// One registered media-kind policy for synchronously applying the same binding and persistence
/// semantics as that kind's scanner, scoped strictly to one import's placed files.
/// </summary>
public interface IImportedEntityMaterializationPolicy {
    /// <summary>Incremental scan snapshot owned by this policy.</summary>
    JobType ScanJobType { get; }

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
    Task<ImportedEntityMaterializationResult> MaterializeAsync(
        EntityKind kind,
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Registered, branch-free imported Entity materialization dispatcher.</summary>
public sealed class ImportedEntityMaterializer(
    IEnumerable<IImportedEntityMaterializationPolicy> policies,
    IImportedEntityReadinessPersistence readiness,
    IScanSnapshotStore? snapshots = null,
    IDownstreamNeedsPersistence? processingRoots = null) : IImportedEntityMaterializer {
    private readonly IReadOnlyDictionary<AcquisitionNamingFamily, IImportedEntityMaterializationPolicy> _byFamily =
        policies.ToDictionary(AcquisitionStrategyRegistration.FamilyOf);

    public async Task<ImportedEntityMaterializationResult> MaterializeAsync(
        EntityKind kind,
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken) {
        var family = AcquisitionStrategyRegistration.TryGetNamingFamily(kind);
        if (family is null || !_byFamily.TryGetValue(family.Value, out var policy)) {
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

        if (snapshots is not null) {
            await ImportedScanSnapshot.ApplyAsync(
                snapshots,
                request.Root.Id,
                policy.ScanJobType,
                normalized,
                request.RemovedSourcePaths ?? [],
                cancellationToken);
        }

        var scope = await readiness.ResolveScopeAsync(normalized, cancellationToken);
        var entities = scope.Owners.Count > 0
            ? scope.Owners.Select(owner => new ImportedEntityReference(owner.Id, owner.Kind)).ToArray()
            : request.EntityId is { } entityId
                ? [new ImportedEntityReference(entityId, kind)]
                : [];
        var ancestors = scope.AncestorIds.ToHashSet();
        if (request.EntityId is { } requestedId && entities.All(entity => entity.Id != requestedId)) {
            ancestors.Add(requestedId);
        }

        var reconciliationRoots = await ResolveProcessingRootsAsync(
            entities,
            processingRoots,
            cancellationToken);
        return new ImportedEntityMaterializationResult(
            entities,
            ancestors.ToArray(),
            normalized,
            NormalizeOptional(request.ReplacedSourcePaths),
            NormalizeOptional(request.RemovedSourcePaths),
            reconciliationRoots);
    }

    internal static async Task<IReadOnlyList<ImportedEntityReference>> ResolveProcessingRootsAsync(
        IReadOnlyList<ImportedEntityReference> entities,
        IDownstreamNeedsPersistence? roots,
        CancellationToken cancellationToken) {
        if (roots is null || entities.Count == 0) {
            return entities;
        }

        var resolved = await roots.ResolveEntityProcessingRootsAsync(
            entities.Select(entity => entity.Id).Distinct().ToArray(),
            cancellationToken);
        return resolved
            .Where(root => EntityKindRegistry.TryGet(root.KindCode, out _))
            .Select(root => new ImportedEntityReference(
                root.Id,
                EntityKindRegistry.Require(root.KindCode)))
            .DistinctBy(root => root.Id)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeOptional(IReadOnlyList<string>? paths) =>
        paths is null
            ? []
            : paths.Select(Path.GetFullPath).Distinct(FileSystemPathComparison.Comparer).ToArray();

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
[AcquisitionStrategy(AcquisitionNamingFamily.Book)]
public sealed class ImportedBookMaterializationPolicy(ScanBookJobHandler scan)
    : IImportedEntityMaterializationPolicy {
    public JobType ScanJobType => JobType.ScanBook;

    public Task MaterializeAsync(
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken) =>
        scan.MaterializeImportedPathsAsync(
            context, request.AcquisitionId, request.Root, request.PlacedMediaPaths, cancellationToken);
}

/// <summary>Movie import policy backed by the video scanner's exact-path materialization seam.</summary>
[AcquisitionStrategy(AcquisitionNamingFamily.Movie)]
public sealed class ImportedMovieMaterializationPolicy(ScanLibraryJobHandler scan)
    : IImportedEntityMaterializationPolicy {
    public JobType ScanJobType => JobType.ScanLibrary;

    public Task MaterializeAsync(
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken) =>
        scan.MaterializeImportedPathsAsync(
            context, request.AcquisitionId, request.Root, request.PlacedMediaPaths, cancellationToken);
}

/// <summary>Album import policy backed by the audio scanner's exact-path materialization seam.</summary>
[AcquisitionStrategy(AcquisitionNamingFamily.Music)]
public sealed class ImportedAlbumMaterializationPolicy(ScanAudioJobHandler scan)
    : IImportedEntityMaterializationPolicy {
    public JobType ScanJobType => JobType.ScanAudio;

    public Task MaterializeAsync(
        JobContext context,
        ImportedEntityMaterializationRequest request,
        CancellationToken cancellationToken) =>
        scan.MaterializeImportedPathsAsync(
            context,
            request.AcquisitionId,
            request.Root,
            request.PlacedMediaPaths,
            cancellationToken,
            request.RequestedAudioTrackIdsByPath);
}
