using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Materializes static card thumbnails outside the API read path. An Entity's own artwork wins;
/// a persisted reader cover page is next; otherwise up to four direct-child thumbnails are folded
/// into a collage. Requested subtrees are rendered bottom-up and their ancestors afterward, so
/// one scan-level request coalesces many child changes into one final parent render.
/// </summary>
public sealed class GridThumbnailService(
    PrismediaDbContext db,
    AssetPathService assets,
    IImageThumbnailGenerator imageThumbnails,
    SkiaThumbnailCollageComposer collages) : IGridThumbnailService {
    private const int GridThumbnailMaxWidth = 480;
    private const int GridThumbnail2xMaxWidth = 960;
    private const int GridThumbnailJpegQuality = 80;
    private const int MaxCollageSources = 4;

    private static readonly EntityFileRole[] LoadedRoles =
    [
        EntityFileRole.Source,
        EntityFileRole.Thumbnail,
        EntityFileRole.Poster,
        EntityFileRole.Cover,
        EntityFileRole.Logo,
        EntityFileRole.Backdrop,
        EntityFileRole.GridThumbnail,
        EntityFileRole.GridThumbnail2x
    ];

    /// <inheritdoc />
    public Task EnsureAsync(Guid entityId, CancellationToken cancellationToken) =>
        EnsureManyAsync([entityId], cancellationToken);

    /// <inheritdoc />
    public async Task EnsureManyAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var requestedIds = entityIds
            .Where(entityId => entityId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (requestedIds.Length == 0) return;

        var plan = await LoadGenerationPlanAsync(requestedIds, cancellationToken);
        if (plan.GenerationNodes.Count == 0) return;
        await using var generationLocks = await AcquireGenerationLocksAsync(plan, cancellationToken);

        var files = await db.EntityFiles
            .Where(file => plan.FileOwnerIds.Contains(file.EntityId) && LoadedRoles.Contains(file.Role))
            .ToListAsync(cancellationToken);
        var filesByEntity = files
            .GroupBy(file => file.EntityId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var pageSources = await LoadPageSourcesAsync(
            plan.GenerationNodes.Keys.ToArray(),
            filesByEntity,
            cancellationToken);
        var generationIds = plan.GenerationNodes.Keys.ToArray();
        var rollupUpdatedAt = await db.EntityRollups.AsNoTracking()
            .Where(rollup => generationIds.Contains(rollup.EntityId))
            .ToDictionaryAsync(rollup => rollup.EntityId, rollup => rollup.UpdatedAt, cancellationToken);

        foreach (var node in OrderDeepestFirst(plan.GenerationNodes)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (PreservesOriginalArtwork(node.KindCode)) continue;

            var ownCover = ResolveOwnCover(node.Id, filesByEntity);
            var pageCover = ownCover is null ? pageSources.GetValueOrDefault(node.Id) : null;
            var childSources = ownCover is null && pageCover is null
                ? ResolveChildSources(node.Id, plan.AllNodes, filesByEntity)
                : [];
            IReadOnlyList<ThumbnailSource> sources = ownCover is not null
                ? [ownCover]
                : pageCover is not null
                    ? [pageCover]
                    : childSources;

            if (sources.Count == 0) {
                RemoveVariants(node.Id, filesByEntity);
                continue;
            }

            var aggregateFreshness = ownCover is null && pageCover is null
                ? rollupUpdatedAt.GetValueOrDefault(node.Id, node.UpdatedAt)
                : DateTimeOffset.MinValue;
            var expectedFreshness = sources
                .Select(source => source.UpdatedAt)
                .Append(aggregateFreshness)
                .Max();
            if (VariantsAreCurrent(node.Id, expectedFreshness, filesByEntity)) {
                continue;
            }

            var presentation = EntityKindRegistry.TryDescribe(node.KindCode, out var definition)
                ? definition.Presentation
                : null;
            var generated = await GeneratePairAsync(
                node.Id,
                sources,
                presentation?.ThumbnailWidth ?? 1,
                presentation?.ThumbnailHeight ?? 1,
                cancellationToken);
            if (generated is null) continue;

            var now = DateTimeOffset.UtcNow;
            UpsertVariant(
                node.Id,
                EntityFileRole.GridThumbnail,
                AssetPathService.GridThumbnailUrl(node.Id),
                generated.Value.StandardSize,
                now,
                filesByEntity);
            UpsertVariant(
                node.Id,
                EntityFileRole.GridThumbnail2x,
                AssetPathService.GridThumbnail2xUrl(node.Id),
                generated.Value.LargeSize,
                now,
                filesByEntity);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IAsyncDisposable> AcquireGenerationLocksAsync(
        GenerationPlan plan,
        CancellationToken cancellationToken) {
        if (!db.Database.IsNpgsql()) return EmptyAsyncDisposable.Instance;

        var lockKeys = plan.GenerationNodes.Values
            .Where(node => node.ParentEntityId is null || !plan.GenerationNodes.ContainsKey(node.ParentEntityId.Value))
            .Select(node => AdvisoryLockKey(node.Id))
            .Distinct()
            .Order()
            .ToArray();
        if (lockKeys.Length == 0) return EmptyAsyncDisposable.Instance;

        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection) await connection.OpenAsync(cancellationToken);

        var acquired = new List<long>(lockKeys.Length);
        try {
            foreach (var lockKey in lockKeys) {
                await ExecuteAdvisoryLockCommandAsync(
                    connection,
                    "SELECT pg_advisory_lock(@key);",
                    lockKey,
                    cancellationToken);
                acquired.Add(lockKey);
            }

            return new AdvisoryLockLease(connection, acquired, closeConnection);
        } catch {
            await AdvisoryLockLease.ReleaseAsync(connection, acquired, closeConnection);
            throw;
        }
    }

    private static async Task ExecuteAdvisoryLockCommandAsync(
        DbConnection connection,
        string commandText,
        long lockKey,
        CancellationToken cancellationToken) {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.DbType = DbType.Int64;
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long AdvisoryLockKey(Guid entityId) =>
        BitConverter.ToInt64(entityId.ToByteArray()) ^ 0x475249445448554D;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListEntitiesNeedingRefreshAsync(
        CancellationToken cancellationToken) {
        var nodes = await db.Entities.AsNoTracking()
            .Select(ToNodeExpression)
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);
        if (nodes.Count == 0) return [];

        var files = await db.EntityFiles.AsNoTracking()
            .Where(file => LoadedRoles.Contains(file.Role))
            .ToArrayAsync(cancellationToken);
        var filesByEntity = files
            .GroupBy(file => file.EntityId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var pageHeaders = await db.EntityPageManifests.AsNoTracking()
            .ToDictionaryAsync(row => row.EntityId, row => row.UpdatedAt, cancellationToken);
        var rollupUpdatedAt = await db.EntityRollups.AsNoTracking()
            .ToDictionaryAsync(row => row.EntityId, row => row.UpdatedAt, cancellationToken);
        var childrenByParent = nodes.Values
            .Where(node => node.ParentEntityId is not null)
            .GroupBy(node => node.ParentEntityId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(node => node.SortOrder ?? int.MaxValue)
                    .ThenBy(node => node.CreatedAt)
                    .ThenBy(node => node.Id)
                    .ToArray());
        var candidateByEntity = new Dictionary<Guid, ThumbnailSource>();
        var needed = new HashSet<Guid>();

        foreach (var node in OrderDeepestFirst(nodes)) {
            var ownCover = ResolveOwnCover(node.Id, filesByEntity);
            ThumbnailSource? candidate = ownCover;
            if (candidate is null && pageHeaders.TryGetValue(node.Id, out var pageUpdatedAt) &&
                HasUsableSource(node.Id, filesByEntity)) {
                candidate = new ThumbnailSource(string.Empty, pageUpdatedAt);
            }
            if (candidate is null && childrenByParent.TryGetValue(node.Id, out var children)) {
                var childCandidates = children
                    .Select(child => candidateByEntity.GetValueOrDefault(child.Id))
                    .Where(source => source is not null)
                    .Select(source => source!)
                    .Take(MaxCollageSources)
                    .ToArray();
                if (childCandidates.Length > 0) {
                    candidate = new ThumbnailSource(
                        string.Empty,
                        childCandidates
                            .Select(source => source.UpdatedAt)
                            .Append(rollupUpdatedAt.GetValueOrDefault(node.Id, node.UpdatedAt))
                            .Max());
                }
            }

            if (candidate is not null) {
                candidateByEntity[node.Id] = candidate;
            }
            if (PreservesOriginalArtwork(node.KindCode)) continue;

            var hasVariants = HasVariantRows(node.Id, filesByEntity);
            if (candidate is null) {
                if (hasVariants) needed.Add(node.Id);
            } else if (!VariantsAreCurrent(node.Id, candidate.UpdatedAt, filesByEntity)) {
                needed.Add(node.Id);
            }
        }

        return needed
            .Where(entityId => !HasNeededAncestor(entityId, needed, nodes))
            .OrderBy(entityId => nodes[entityId].CreatedAt)
            .ThenBy(entityId => entityId)
            .ToArray();
    }

    private async Task<GenerationPlan> LoadGenerationPlanAsync(
        IReadOnlyCollection<Guid> requestedIds,
        CancellationToken cancellationToken) {
        var generationNodes = await db.Entities.AsNoTracking()
            .Where(entity => requestedIds.Contains(entity.Id))
            .Select(ToNodeExpression)
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);
        var frontier = generationNodes.Keys.ToArray();
        while (frontier.Length > 0) {
            var children = await db.Entities.AsNoTracking()
                .Where(entity => entity.ParentEntityId != null && frontier.Contains(entity.ParentEntityId.Value))
                .Select(ToNodeExpression)
                .ToArrayAsync(cancellationToken);
            frontier = children
                .Where(child => generationNodes.TryAdd(child.Id, child))
                .Select(child => child.Id)
                .ToArray();
        }

        var ancestorFrontier = generationNodes.Values
            .Where(node => requestedIds.Contains(node.Id) && node.ParentEntityId is not null)
            .Select(node => node.ParentEntityId!.Value)
            .Distinct()
            .ToArray();
        while (ancestorFrontier.Length > 0) {
            var ancestors = await db.Entities.AsNoTracking()
                .Where(entity => ancestorFrontier.Contains(entity.Id))
                .Select(ToNodeExpression)
                .ToArrayAsync(cancellationToken);
            var added = ancestors
                .Where(ancestor => generationNodes.TryAdd(ancestor.Id, ancestor))
                .ToArray();
            ancestorFrontier = added
                .Select(ancestor => ancestor.ParentEntityId)
                .Where(parentId => parentId is not null)
                .Select(parentId => parentId!.Value)
                .Distinct()
                .ToArray();
        }

        var ancestorIds = generationNodes.Values
            .Where(node => !requestedIds.Contains(node.Id))
            .Select(node => node.Id)
            .ToArray();
        var allNodes = new Dictionary<Guid, ThumbnailNode>(generationNodes);
        if (ancestorIds.Length > 0) {
            var siblingCandidates = await db.Entities.AsNoTracking()
                .Where(entity => entity.ParentEntityId != null && ancestorIds.Contains(entity.ParentEntityId.Value))
                .Select(ToNodeExpression)
                .ToArrayAsync(cancellationToken);
            foreach (var sibling in siblingCandidates) allNodes.TryAdd(sibling.Id, sibling);
        }

        return new GenerationPlan(
            generationNodes,
            allNodes,
            allNodes.Keys.ToArray());
    }

    private static readonly System.Linq.Expressions.Expression<Func<EntityRow, ThumbnailNode>> ToNodeExpression =
        entity => new ThumbnailNode(
            entity.Id,
            entity.KindCode,
            entity.ParentEntityId,
            entity.SortOrder,
            entity.CreatedAt,
            entity.UpdatedAt);

    private async Task<IReadOnlyDictionary<Guid, ThumbnailSource>> LoadPageSourcesAsync(
        IReadOnlyCollection<Guid> entityIds,
        IReadOnlyDictionary<Guid, List<EntityFileRow>> filesByEntity,
        CancellationToken cancellationToken) {
        var headers = await db.EntityPageManifests.AsNoTracking()
            .Where(header => entityIds.Contains(header.EntityId))
            .ToArrayAsync(cancellationToken);
        if (headers.Length == 0) return new Dictionary<Guid, ThumbnailSource>();

        var headerByEntity = headers.ToDictionary(header => header.EntityId);
        var headerIds = headerByEntity.Keys.ToArray();
        var pages = await db.EntityPageEntries.AsNoTracking()
            .Where(page => headerIds.Contains(page.EntityId))
            .ToArrayAsync(cancellationToken);
        var result = new Dictionary<Guid, ThumbnailSource>();
        foreach (var page in pages.Where(page =>
                     headerByEntity.TryGetValue(page.EntityId, out var header) &&
                     page.Ordinal == header.CoverOrdinal)) {
            var source = filesByEntity.GetValueOrDefault(page.EntityId)?
                .SingleOrDefault(file => file.Role == EntityFileRole.Source);
            if (source is null) continue;

            var path = Directory.Exists(source.Path)
                ? ResolveDirectoryMember(source.Path, page.ArchiveMember)
                : EntitySourcePath.ArchiveMember(source.Path, page.ArchiveMember);
            if (path is null || !SourceExists(path)) continue;
            result[page.EntityId] = new ThumbnailSource(path, headerByEntity[page.EntityId].UpdatedAt);
        }

        return result;
    }

    private ThumbnailSource? ResolveOwnCover(
        Guid entityId,
        IReadOnlyDictionary<Guid, List<EntityFileRow>> filesByEntity) {
        var cover = EntityCoverSelection.Select(
            filesByEntity.GetValueOrDefault(entityId)?
                .Where(file => EntityCoverSelection.CoverRoles.Contains(file.Role)) ?? []);
        if (cover is null) return null;
        var path = assets.ResolveAssetDiskPath(cover.Path);
        return path is not null && File.Exists(path)
            ? new ThumbnailSource(path, cover.UpdatedAt)
            : null;
    }

    private IReadOnlyList<ThumbnailSource> ResolveChildSources(
        Guid parentId,
        IReadOnlyDictionary<Guid, ThumbnailNode> nodes,
        IReadOnlyDictionary<Guid, List<EntityFileRow>> filesByEntity) =>
        nodes.Values
            .Where(node => node.ParentEntityId == parentId)
            .OrderBy(node => node.SortOrder ?? int.MaxValue)
            .ThenBy(node => node.CreatedAt)
            .ThenBy(node => node.Id)
            .Select(node => ResolveContributor(node.Id, filesByEntity))
            .Where(source => source is not null)
            .Select(source => source!)
            .Take(MaxCollageSources)
            .ToArray();

    private ThumbnailSource? ResolveContributor(
        Guid entityId,
        IReadOnlyDictionary<Guid, List<EntityFileRow>> filesByEntity) {
        var ownCover = ResolveOwnCover(entityId, filesByEntity);
        if (ownCover is not null) return ownCover;

        var variants = filesByEntity.GetValueOrDefault(entityId);
        var grid = variants?.FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail2x)
            ?? variants?.FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail);
        if (grid is null) return null;
        var path = assets.ResolveAssetDiskPath(grid.Path);
        return path is not null && File.Exists(path)
            ? new ThumbnailSource(path, grid.UpdatedAt)
            : null;
    }

    private async Task<GeneratedPair?> GeneratePairAsync(
        Guid entityId,
        IReadOnlyList<ThumbnailSource> sources,
        int aspectWidth,
        int aspectHeight,
        CancellationToken cancellationToken) {
        var operationId = Guid.NewGuid().ToString("N");
        var standardPath = assets.GridThumbnailPath(entityId);
        var largePath = assets.GridThumbnail2xPath(entityId);
        var standardTemp = Path.Combine(
            Path.GetDirectoryName(standardPath)!,
            $"{Path.GetFileNameWithoutExtension(standardPath)}.tmp-{operationId}.jpg");
        var largeTemp = Path.Combine(
            Path.GetDirectoryName(largePath)!,
            $"{Path.GetFileNameWithoutExtension(largePath)}.tmp-{operationId}.jpg");
        try {
            bool standardGenerated;
            bool largeGenerated;
            if (sources.Count == 1) {
                standardGenerated = await imageThumbnails.GenerateAsync(
                    sources[0].Path,
                    standardTemp,
                    GridThumbnailMaxWidth,
                    GridThumbnailJpegQuality,
                    cancellationToken);
                largeGenerated = standardGenerated && await imageThumbnails.GenerateAsync(
                    sources[0].Path,
                    largeTemp,
                    GridThumbnail2xMaxWidth,
                    GridThumbnailJpegQuality,
                    cancellationToken);
            } else {
                var standardHeight = Math.Max(1, GridThumbnailMaxWidth * aspectHeight / aspectWidth);
                var largeHeight = Math.Max(1, GridThumbnail2xMaxWidth * aspectHeight / aspectWidth);
                var paths = sources.Select(source => source.Path).ToArray();
                standardGenerated = collages.Compose(
                    paths,
                    standardTemp,
                    GridThumbnailMaxWidth,
                    standardHeight,
                    GridThumbnailJpegQuality);
                largeGenerated = standardGenerated && collages.Compose(
                    paths,
                    largeTemp,
                    GridThumbnail2xMaxWidth,
                    largeHeight,
                    GridThumbnailJpegQuality);
            }

            if (!standardGenerated || !largeGenerated) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(standardPath)!);
            File.Move(standardTemp, standardPath, overwrite: true);
            File.Move(largeTemp, largePath, overwrite: true);
            return new GeneratedPair(
                new FileInfo(standardPath).Length,
                new FileInfo(largePath).Length);
        } finally {
            TryDelete(standardTemp);
            TryDelete(largeTemp);
        }
    }

    private void UpsertVariant(
        Guid entityId,
        EntityFileRole role,
        string url,
        long size,
        DateTimeOffset now,
        IDictionary<Guid, List<EntityFileRow>> filesByEntity) {
        if (!filesByEntity.TryGetValue(entityId, out var files)) {
            files = [];
            filesByEntity[entityId] = files;
        }

        var existing = files.FirstOrDefault(file => file.Role == role);
        if (existing is null) {
            existing = new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = role,
                CreatedAt = now
            };
            db.EntityFiles.Add(existing);
            files.Add(existing);
        }

        existing.Path = url;
        existing.MimeType = MediaContentTypes.ImageJpeg;
        existing.SizeBytes = size;
        existing.Source = FileSourceKind.Scan.ToCode();
        existing.UpdatedAt = now;
    }

    private void RemoveVariants(Guid entityId, IDictionary<Guid, List<EntityFileRow>> filesByEntity) {
        if (!filesByEntity.TryGetValue(entityId, out var files)) return;
        var variants = files
            .Where(file => file.Role is EntityFileRole.GridThumbnail or EntityFileRole.GridThumbnail2x)
            .ToArray();
        if (variants.Length == 0) return;

        db.EntityFiles.RemoveRange(variants);
        foreach (var variant in variants) files.Remove(variant);
        TryDelete(assets.GridThumbnailPath(entityId));
        TryDelete(assets.GridThumbnail2xPath(entityId));
    }

    private bool VariantsAreCurrent(
        Guid entityId,
        DateTimeOffset expectedFreshness,
        IReadOnlyDictionary<Guid, List<EntityFileRow>> filesByEntity) {
        var files = filesByEntity.GetValueOrDefault(entityId);
        var standard = files?.FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail);
        var large = files?.FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail2x);
        return standard is not null && large is not null &&
            standard.UpdatedAt >= expectedFreshness && large.UpdatedAt >= expectedFreshness &&
            AssetExists(standard) && AssetExists(large);
    }

    private static bool HasVariantRows(
        Guid entityId,
        IReadOnlyDictionary<Guid, List<EntityFileRow>> filesByEntity) =>
        filesByEntity.GetValueOrDefault(entityId)?.Any(file =>
            file.Role is EntityFileRole.GridThumbnail or EntityFileRole.GridThumbnail2x) == true;

    private bool AssetExists(EntityFileRow file) {
        var path = assets.ResolveAssetDiskPath(file.Path);
        return path is not null && File.Exists(path);
    }

    private static bool HasUsableSource(
        Guid entityId,
        IReadOnlyDictionary<Guid, List<EntityFileRow>> filesByEntity) =>
        filesByEntity.GetValueOrDefault(entityId)?
            .Any(file => file.Role == EntityFileRole.Source &&
                (File.Exists(file.Path) || Directory.Exists(file.Path))) == true;

    private static IReadOnlyList<ThumbnailNode> OrderDeepestFirst(
        IReadOnlyDictionary<Guid, ThumbnailNode> nodes) =>
        nodes.Values
            .OrderByDescending(node => Depth(node, nodes))
            .ThenBy(node => node.SortOrder ?? int.MaxValue)
            .ThenBy(node => node.CreatedAt)
            .ThenBy(node => node.Id)
            .ToArray();

    private static int Depth(
        ThumbnailNode node,
        IReadOnlyDictionary<Guid, ThumbnailNode> nodes) {
        var depth = 0;
        var seen = new HashSet<Guid> { node.Id };
        var parentId = node.ParentEntityId;
        while (parentId is { } id && seen.Add(id) && nodes.TryGetValue(id, out var parent)) {
            depth++;
            parentId = parent.ParentEntityId;
        }
        return depth;
    }

    private static bool HasNeededAncestor(
        Guid entityId,
        IReadOnlySet<Guid> needed,
        IReadOnlyDictionary<Guid, ThumbnailNode> nodes) {
        var seen = new HashSet<Guid> { entityId };
        var parentId = nodes.GetValueOrDefault(entityId)?.ParentEntityId;
        while (parentId is { } id && seen.Add(id) && nodes.TryGetValue(id, out var parent)) {
            if (needed.Contains(id)) return true;
            parentId = parent.ParentEntityId;
        }
        return false;
    }

    private static string? ResolveDirectoryMember(string directory, string member) {
        var root = Path.GetFullPath(directory);
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            member.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, candidate);
        return relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? null
            : candidate;
    }

    private static bool SourceExists(string path) =>
        EntitySourcePath.TrySplitArchiveMember(path, out var archivePath, out _)
            ? File.Exists(archivePath)
            : File.Exists(path);

    private static bool PreservesOriginalArtwork(string? kindCode) =>
        EntityKindRegistry.TryDescribe(kindCode, out var definition) &&
        definition.Presentation.ArtworkSurface == EntityArtworkSurface.BrandPlate;

    private static void TryDelete(string path) {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed record ThumbnailNode(
        Guid Id,
        string KindCode,
        Guid? ParentEntityId,
        int? SortOrder,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ThumbnailSource(string Path, DateTimeOffset UpdatedAt);

    private sealed record GenerationPlan(
        IReadOnlyDictionary<Guid, ThumbnailNode> GenerationNodes,
        IReadOnlyDictionary<Guid, ThumbnailNode> AllNodes,
        IReadOnlyCollection<Guid> FileOwnerIds);

    private readonly record struct GeneratedPair(long StandardSize, long LargeSize);

    private sealed class AdvisoryLockLease(
        DbConnection connection,
        IReadOnlyList<long> lockKeys,
        bool closeConnection) : IAsyncDisposable {
        public async ValueTask DisposeAsync() =>
            await ReleaseAsync(connection, lockKeys, closeConnection);

        internal static async Task ReleaseAsync(
            DbConnection connection,
            IReadOnlyList<long> lockKeys,
            bool closeConnection) {
            try {
                foreach (var lockKey in lockKeys.Reverse()) {
                    await ExecuteAdvisoryLockCommandAsync(
                        connection,
                        "SELECT pg_advisory_unlock(@key);",
                        lockKey,
                        CancellationToken.None);
                }
            } finally {
                if (closeConnection) await connection.CloseAsync();
            }
        }
    }

    private sealed class EmptyAsyncDisposable : IAsyncDisposable {
        internal static EmptyAsyncDisposable Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
