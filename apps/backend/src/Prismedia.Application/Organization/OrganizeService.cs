using Prismedia.Contracts.Organize;
using Prismedia.Domain.Entities;
using Prismedia.Application.Files;

namespace Prismedia.Application.Organization;

/// <summary>
/// Application use-case service that computes generic entity organization plans from source
/// paths, structural parents, and entity-kind storage metadata, and applies them by moving
/// source files or folders on disk. Raw persistence reads and the post-move database-side
/// path rewrite are delegated to <see cref="IOrganizePersistence"/>.
/// </summary>
public sealed class OrganizeService {
    private const string Ready = "ready";
    private const string Unchanged = "unchanged";
    private const string Skipped = "skipped";
    private const string Applied = "applied";
    private const string Failed = "failed";

    private readonly IOrganizePersistence _persistence;
    private readonly EntitySourcePathMutationCoordinator _sourcePathMutations;

    /// <summary>
    /// Creates the service over the organize persistence port.
    /// </summary>
    public OrganizeService(
        IOrganizePersistence persistence,
        EntitySourcePathMutationCoordinator sourcePathMutations) {
        _persistence = persistence;
        _sourcePathMutations = sourcePathMutations;
    }

    /// <summary>
    /// Builds a dry-run organization plan without moving any files.
    /// </summary>
    public async Task<OrganizePlanResponse> PlanAsync(
        OrganizePlanRequest request,
        CancellationToken cancellationToken) {
        var plan = await BuildPlanAsync(request, cancellationToken);
        return new OrganizePlanResponse(plan);
    }

    /// <summary>
    /// Applies a computed organization plan by moving source files and folders on disk and
    /// updating stored paths. Items whose ancestors are also being moved are skipped so a
    /// second run can reposition them once the parent move has completed.
    /// </summary>
    public async Task<OrganizeApplyResponse> ApplyAsync(
        OrganizePlanRequest request,
        CancellationToken cancellationToken) {
        var plan = await BuildPlanAsync(request, cancellationToken);
        var ancestorMoveSources = plan
            .Where(item => item.Status == Ready)
            .Select(item => item.SourcePath)
            .OrderBy(path => path.Length)
            .ToArray();
        var results = new List<OrganizePlanItem>(plan.Count);
        var applied = 0;

        foreach (var item in plan) {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Status != Ready) {
                results.Add(item);
                continue;
            }

            if (ancestorMoveSources.Any(path =>
                    !SamePath(path, item.SourcePath) &&
                    IsSubPathOf(item.SourcePath, path))) {
                results.Add(item with {
                    Status = Skipped,
                    Reason = "A parent folder is being moved first. Run organize again to apply child renames.",
                });
                continue;
            }

            try {
                var executed = await _sourcePathMutations.ExecuteAsync(
                    item.SourcePath,
                    [item.EntityId],
                    async token => {
                        MoveSource(item.SourcePath, item.TargetPath);
                        await _persistence.ApplyPathPrefixRewriteAsync(item.SourcePath, item.TargetPath, token);
                    },
                    cancellationToken);
                if (!executed) {
                    results.Add(item with {
                        Status = Failed,
                        Reason = "The linked Entity changed while the organization plan was being applied.",
                    });
                    continue;
                }

                results.Add(item with { Status = Applied });
                applied++;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                results.Add(item with { Status = Failed, Reason = ex.Message });
            }
        }

        return new OrganizeApplyResponse(results, applied, results.Count - applied);
    }

    private async Task<IReadOnlyList<OrganizePlanItem>> BuildPlanAsync(
        OrganizePlanRequest request,
        CancellationToken cancellationToken) {
        var roots = await _persistence.ListRootsAsync(request.RootId, cancellationToken);
        var rootPaths = roots
            .Select(root => (root.Id, Path: Normalize(root.Path)))
            .OrderByDescending(root => root.Path.Length)
            .ToArray();

        var entities = await _persistence.ListActiveEntitiesAsync(request.EntityId, cancellationToken);
        var entityIds = entities.Select(entity => entity.Id).ToArray();
        var sourceFiles = await _persistence.ListSourceFilesAsync(entityIds, cancellationToken);

        var entityById = entities.ToDictionary(entity => entity.Id);
        var sourceByEntityId = sourceFiles.ToDictionary(file => file.EntityId);
        var memo = new Dictionary<Guid, OrganizePlanItem?>();

        return entities
            .Select(entity => BuildItem(entity.Id, entityById, sourceByEntityId, rootPaths, memo))
            .OfType<OrganizePlanItem>()
            .OrderBy(item => item.SourcePath, FileSystemPathComparison.Comparer)
            .ToArray();
    }

    private static OrganizePlanItem? BuildItem(
        Guid entityId,
        IReadOnlyDictionary<Guid, OrganizeEntityRow> entityById,
        IReadOnlyDictionary<Guid, OrganizeSourceFile> sourceByEntityId,
        IReadOnlyList<(Guid Id, string Path)> rootPaths,
        IDictionary<Guid, OrganizePlanItem?> memo) {
        if (memo.TryGetValue(entityId, out var cached)) {
            return cached;
        }

        if (!entityById.TryGetValue(entityId, out var entity) ||
            !sourceByEntityId.TryGetValue(entityId, out var sourceFile) ||
            !EntityKindRegistry.TryGet(entity.KindCode, out var kind)) {
            memo[entityId] = null;
            return null;
        }

        var storageShape = EntityKindRegistry.Describe(kind).StorageShape;
        var sourcePath = Normalize(sourceFile.Path);
        if (storageShape is EntityStorageShape.None or EntityStorageShape.ArchiveEntry) {
            var skipped = NewItem(entity, storageShape, sourcePath, sourcePath, Skipped,
                storageShape == EntityStorageShape.ArchiveEntry
                    ? "Archive entries are not moved independently."
                    : "This entity kind has no direct filesystem storage.");
            memo[entityId] = skipped;
            return skipped;
        }

        var targetContainer = ResolveTargetContainer(entity, entityById, sourceByEntityId, rootPaths, memo);
        if (targetContainer is null) {
            var skipped = NewItem(entity, storageShape, sourcePath, sourcePath, Skipped,
                "No library root contains the entity source path.");
            memo[entityId] = skipped;
            return skipped;
        }

        var targetPath = storageShape switch {
            EntityStorageShape.Folder => Path.Combine(targetContainer, SafePathSegment(entity.Title, entity.Id)),
            EntityStorageShape.File or EntityStorageShape.Archive => Path.Combine(
                targetContainer,
                SafePathSegment(entity.Title, entity.Id) + Path.GetExtension(sourcePath)),
            _ => sourcePath,
        };

        var status = SamePath(sourcePath, targetPath) ? Unchanged : Ready;
        var item = NewItem(entity, storageShape, sourcePath, Normalize(targetPath), status, null);
        memo[entityId] = item;
        return item;
    }

    private static string? ResolveTargetContainer(
        OrganizeEntityRow entity,
        IReadOnlyDictionary<Guid, OrganizeEntityRow> entityById,
        IReadOnlyDictionary<Guid, OrganizeSourceFile> sourceByEntityId,
        IReadOnlyList<(Guid Id, string Path)> rootPaths,
        IDictionary<Guid, OrganizePlanItem?> memo) {
        if (entity.ParentEntityId is { } parentId &&
            entityById.TryGetValue(parentId, out var parent) &&
            EntityKindRegistry.TryGet(parent.KindCode, out var parentKind)) {
            var parentStorageShape = EntityKindRegistry.Describe(parentKind).StorageShape;
            var parentItem = BuildItem(parentId, entityById, sourceByEntityId, rootPaths, memo);
            if (parentItem is null) {
                return null;
            }

            return parentStorageShape == EntityStorageShape.Folder
                ? parentItem.TargetPath
                : Path.GetDirectoryName(parentItem.TargetPath);
        }

        if (!sourceByEntityId.TryGetValue(entity.Id, out var sourceFile)) {
            return null;
        }

        var sourcePath = Normalize(sourceFile.Path);
        var root = rootPaths.FirstOrDefault(rootPath => IsSubPathOf(sourcePath, rootPath.Path) || SamePath(sourcePath, rootPath.Path));
        return root.Path;
    }

    private static OrganizePlanItem NewItem(
        OrganizeEntityRow entity,
        EntityStorageShape storageShape,
        string sourcePath,
        string targetPath,
        string status,
        string? reason) =>
        new(
            entity.Id,
            entity.KindCode.DecodeAs<EntityKind>(),
            entity.Title,
            storageShape.ToCode(),
            sourcePath,
            targetPath,
            status,
            reason);

    private static void MoveSource(string sourcePath, string targetPath) {
        if (SamePath(sourcePath, targetPath)) {
            return;
        }

        var parent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parent)) {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(targetPath) || Directory.Exists(targetPath)) {
            throw new IOException($"Target path already exists: {targetPath}");
        }

        if (Directory.Exists(sourcePath)) {
            Directory.Move(sourcePath, targetPath);
            return;
        }

        if (File.Exists(sourcePath)) {
            File.Move(sourcePath, targetPath);
            return;
        }

        throw new IOException($"Source path does not exist: {sourcePath}");
    }

    private static string SafePathSegment(string title, Guid entityId) {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(title
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned)
            ? entityId.ToString("N")
            : cleaned;
    }

    private static string Normalize(string path) => Path.GetFullPath(path);

    private static bool SamePath(string left, string right) =>
        FileSystemPathComparison.Equals(
            Path.TrimEndingDirectorySeparator(Normalize(left)),
            Path.TrimEndingDirectorySeparator(Normalize(right)));

    private static bool IsSubPathOf(string path, string parent) {
        var fullPath = Path.TrimEndingDirectorySeparator(Normalize(path));
        var fullParent = Path.TrimEndingDirectorySeparator(Normalize(parent));
        return fullPath.StartsWith(
            fullParent + Path.DirectorySeparatorChar,
            FileSystemPathComparison.Comparison);
    }
}
