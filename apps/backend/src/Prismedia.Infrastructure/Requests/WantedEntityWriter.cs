using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Creates wanted library entities for request commits and populates them through the shared
/// metadata-apply cascade. A wanted skeleton is a plain entity row flagged Wanted with the provider
/// external id stamped — deliberately minimal, because <see cref="ApplyProposalAsync"/> then finds it
/// external-id-first (the identify apply rule) and fills in fields, artwork, and relationships exactly
/// the way Identify would. No library root or source file is attached until the acquisition imports.
/// Kind-specific persistence (which detail row a kind carries) is the only per-kind branch here.
/// </summary>
/// <param name="db">Scoped Prismedia unit of work.</param>
/// <param name="apply">Shared metadata proposal application service.</param>
/// <param name="externalIdentities">Canonical external-identity resolver and writer.</param>
/// <param name="providerIdentities">Persisted exact plugin route selected for the Entity.</param>
/// <param name="identityRouter">Manifest-driven fallback for Entities created before bindings existed.</param>
/// <param name="hierarchy">Canonical kind-agnostic Entity hierarchy reader used for source ownership.</param>
public sealed class WantedEntityWriter(
    PrismediaDbContext db,
    EntityMetadataApplyService apply,
    IEntityExternalIdentityStore externalIdentities,
    IEntityProviderIdentityStore providerIdentities,
    IPluginIdentityRouter identityRouter,
    IEntityHierarchyReader hierarchy,
    IEntityLifecycleMutationLease? lifecycle = null) : IWantedEntityWriter {
    private readonly IEntityLifecycleMutationLease lifecycleLease =
        lifecycle ?? new EfEntityLifecycleMutationLease(db, hierarchy);
    /// <summary>Wanted container kinds whose empty placeholders are pruned with their last child (from the request registry).</summary>
    private static readonly HashSet<string> ContainerKindCodes = RequestKindRegistry.All
        .Where(descriptor => descriptor.IsContainer)
        .Select(descriptor => descriptor.WantedEntityKind.ToCode())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<WantedEntityResult> EnsureAsync(
        EntityKind kind, ExternalIdentity identity, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken) {
        var kindCode = kind.ToCode();
        var resolution = await externalIdentities.ResolveAsync(kind, [identity], parentEntityId, cancellationToken);
        if (resolution.Status == ExternalIdentityResolutionStatus.Ambiguous) {
            throw new ExternalIdentityAmbiguityException(kind, resolution);
        }

        var entity = resolution.EntityId is { } matchedId
            ? await db.Entities.FindAsync([matchedId], cancellationToken)
            : await FindByTitleAsync(kindCode, title, parentEntityId, matchTitleKindWide, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (entity is not null) {
            WantedEntityResult? result = null;
            await ExecuteIfLifecycleMutableAsync(
                entity.Id,
                async leaseCancellationToken => {
                    var current = await db.Entities.FirstOrDefaultAsync(
                        row => row.Id == entity.Id,
                        leaseCancellationToken);
                    if (current is null) {
                        return;
                    }
                    var hasFile = await HasSourceFileAsync(current.Id, leaseCancellationToken);

                    // A title-matched entity (e.g. a scanned author/artist folder with no provider ids yet)
                    // gains the provider id so every later lookup resolves it id-first.
                    await StampExternalIdAsync(current.Id, identity, leaseCancellationToken);

                    // Re-requesting a still-parentless wanted leaf through its container adopts it under
                    // the container, but never while a lifecycle owner controls either ancestry.
                    if (parentEntityId is { } parent
                        && current.ParentEntityId is null
                        && current.IsWanted
                        && !hasFile) {
                        await ThrowIfLifecycleClaimedAsync(parent, leaseCancellationToken);
                        current.ParentEntityId = parent;
                        current.UpdatedAt = now;
                    }

                    await db.SaveChangesAsync(leaseCancellationToken);
                    result = new WantedEntityResult(current.Id, Created: false, hasFile);
                },
                cancellationToken);
            return result ?? throw LifecycleConflict();
        }

        WantedEntityResult? created = null;
        async Task CreateAsync(CancellationToken leaseCancellationToken) {
            entity = new EntityRow {
                Id = Guid.NewGuid(),
                KindCode = kindCode,
                Title = title.Trim(),
                ParentEntityId = parentEntityId,
                IsWanted = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Entities.Add(entity);
            AddDetailRowFor(kind, entity.Id);

            await StampExternalIdAsync(entity.Id, identity, leaseCancellationToken);
            await db.SaveChangesAsync(leaseCancellationToken);
            created = new WantedEntityResult(entity.Id, Created: true, HasFile: false);
        }

        if (parentEntityId is { } parentId) {
            await ExecuteIfLifecycleMutableAsync(parentId, CreateAsync, cancellationToken);
        } else {
            await CreateAsync(cancellationToken);
        }
        return created ?? throw LifecycleConflict();
    }

    /// <inheritdoc />
    public async Task<bool> BindProviderIdentityAsync(
        Guid entityId,
        PluginIdentityRoute route,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(route.PluginId)) {
            return false;
        }

        var bound = false;
        await ExecuteIfLifecycleMutableAsync(
            entityId,
            async leaseCancellationToken => {
                var kindCode = await db.Entities.AsNoTracking()
                    .Where(row => row.Id == entityId)
                    .Select(row => row.KindCode)
                    .FirstOrDefaultAsync(leaseCancellationToken);
                if (kindCode is null) {
                    return;
                }

                var persistedIdentities = (await externalIdentities.ListAsync(entityId, leaseCancellationToken))
                    .Select(value => value.Identity)
                    .ToHashSet();
                if (!persistedIdentities.Contains(route.Identity)) {
                    return;
                }

                var exactRoutes = (await identityRouter.ResolveAsync(
                        kindCode,
                        IdentifyAction.LookupId,
                        [route.Identity],
                        leaseCancellationToken))
                    .Where(candidate =>
                        candidate.Identity == route.Identity
                        && string.Equals(candidate.PluginId, route.PluginId, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToArray();
                if (exactRoutes.Length != 1) {
                    return;
                }

                var exactRoute = exactRoutes[0];
                await providerIdentities.SetAsync(
                    entityId,
                    exactRoute.PluginId,
                    exactRoute.Identity,
                    leaseCancellationToken);
                await db.SaveChangesAsync(leaseCancellationToken);
                bound = true;
            },
            cancellationToken);
        return bound;
    }

    /// <summary>
    /// Adds the kind's detail row for a wanted skeleton, always with no library root: root-scoped stale
    /// cleanup must never touch a placeholder, and the import-time scan upsert fills in the real values
    /// (type, format, root) from what actually lands on disk. Kinds without a detail row add nothing.
    /// </summary>
    private void AddDetailRowFor(EntityKind kind, Guid entityId) {
        switch (kind) {
            case EntityKind.Book:
                // Placeholder type/format; the import corrects them from the placed file.
                db.BookDetails.Add(new BookDetailRow {
                    EntityId = entityId,
                    BookType = BookType.Novel,
                    Format = BookFormat.Epub,
                    LibraryRootId = null
                });
                break;
            case EntityKind.AudioLibrary:
                db.AudioLibraryDetails.Add(new AudioLibraryDetailRow { EntityId = entityId, LibraryRootId = null });
                break;
            case EntityKind.MusicArtist:
                db.MusicArtistDetails.Add(new MusicArtistDetailRow { EntityId = entityId, LibraryRootId = null });
                break;
            default:
                // Movie / series and other kinds carry no request-relevant detail row today.
                break;
        }
    }

    public async Task ApplyProposalAsync(Guid entityId, Contracts.Plugins.EntityMetadataProposal proposal, CancellationToken cancellationToken) {
        await ExecuteIfLifecycleMutableAsync(
            entityId,
            async leaseCancellationToken => {
                var fields = ProposalApplySelection.SelectAllPresentFields(proposal);
                var images = ProposalApplySelection.SelectDefaultImages(proposal);
                await apply.ApplyAsync(
                    entityId,
                    proposal,
                    fields,
                    images,
                    leaseCancellationToken);
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs one request/metadata mutation while holding the target + ancestor Entity rows. Managed file
    /// deletion publishes its durable claim under the same row locks, so either this mutation commits
    /// first and deletion revalidates it, or the mutation fails before touching the Entity.
    /// </summary>
    private async Task ExecuteIfLifecycleMutableAsync(
        Guid entityId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken) {
        if (!await lifecycleLease.ExecuteAsync(entityId, mutation, cancellationToken)) {
            throw LifecycleConflict();
        }
    }

    /// <summary>Rejects a secondary parent adoption when that ancestry is under destructive ownership.</summary>
    private async Task ThrowIfLifecycleClaimedAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var ids = new[] { entityId }
            .Concat(await hierarchy.ListAncestorIdsAsync(entityId, cancellationToken))
            .Distinct()
            .ToArray();
        if (await db.Entities.AsNoTracking().AnyAsync(
            row => ids.Contains(row.Id) && row.LifecycleClaimKind != null,
            cancellationToken)) {
            throw LifecycleConflict();
        }
    }

    private static AcquisitionConfigurationException LifecycleConflict() =>
        new(
            Prismedia.Contracts.System.ApiProblemCodes.AcquisitionInvalid,
            "This Entity is being cleaned up. Wait for that operation to finish, then request it again.");

    public async Task<bool> DeleteIfWantedAsync(Guid entityId, CancellationToken cancellationToken) {
        var entity = await db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null || !entity.IsWanted || await HasSourceFileAsync(entityId, cancellationToken)) {
            return false;
        }

        var parentId = entity.ParentEntityId;
        db.Entities.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        // A wanted container (author, artist) that just lost its last child is an empty placeholder;
        // remove it with the work (the scan's empty-grouping pruning would eventually do the same, but
        // not until the next scan).
        if (parentId is { } containerId) {
            var container = await db.Entities.FirstOrDefaultAsync(
                row => row.Id == containerId && ContainerKindCodes.Contains(row.KindCode), cancellationToken);
            if (container is { IsWanted: true } &&
                !await db.Entities.AnyAsync(row => row.ParentEntityId == containerId, cancellationToken) &&
                !await HasSourceFileAsync(containerId, cancellationToken)) {
                db.Entities.Remove(container);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<MonitorableEntity?> GetEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var entity = await db.Entities.AsNoTracking()
            .Where(row => row.Id == entityId)
            .Select(row => new { row.Id, row.KindCode, row.Title, row.ParentEntityId })
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null) {
            return null;
        }

        var identities = (await externalIdentities.ListAsync(entityId, cancellationToken))
            .Select(association => association.Identity)
            .ToArray();
        var binding = await providerIdentities.GetAsync(entityId, cancellationToken);
        // Carry an authoritative binding even when its identity is stale. The tracking catalog validates
        // exact membership and fails it closed; collapsing stale to null would make it look like an unbound
        // legacy Entity and could silently substitute another plugin/identity.
        PluginIdentityRoute? providerIdentity = binding is null
            ? null
            : new PluginIdentityRoute(binding.PluginId, binding.Identity);
        // A persisted binding whose exact value no longer belongs to the Entity is stale, not legacy.
        // Fail closed instead of silently retargeting it through the remaining raw IDs.
        if (binding is null && identities.Length > 0) {
            var routes = await identityRouter.ResolveAsync(
                entity.KindCode,
                IdentifyAction.LookupId,
                identities,
                cancellationToken);
            providerIdentity = routes.Count == 1 ? routes[0] : null;
        }
        var positions = await db.EntityPositions.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .ToDictionaryAsync(row => row.Code, row => row.Value, cancellationToken);
        var hasSource = await HasSourceFileAsync(entityId, cancellationToken);
        return new MonitorableEntity(
            entity.Id, entity.KindCode.DecodeAs<EntityKind>(), entity.Title, identities, hasSource,
            entity.ParentEntityId, positions, providerIdentity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, MonitorEligibilityEntity>> ListMonitorEligibilityEntitiesAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var requestedIds = entityIds.Distinct().ToArray();
        if (requestedIds.Length == 0) {
            return new Dictionary<Guid, MonitorEligibilityEntity>();
        }

        var entities = await db.Entities.AsNoTracking()
            .Where(row => requestedIds.Contains(row.Id))
            .Select(row => new { row.Id, row.KindCode, row.IsWanted })
            .ToArrayAsync(cancellationToken);
        var identityRows = await db.EntityExternalIds.AsNoTracking()
            .Where(row => requestedIds.Contains(row.EntityId))
            .Select(row => new { row.EntityId, row.Provider, row.Value })
            .ToArrayAsync(cancellationToken);
        var bindingRows = await db.EntityProviderIdentities.AsNoTracking()
            .Where(row => requestedIds.Contains(row.EntityId))
            .ToArrayAsync(cancellationToken);

        var identitiesByEntity = identityRows
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => TryIdentity(row.Provider, row.Value))
                    .Where(identity => identity is not null)
                    .Select(identity => identity!)
                    .Distinct()
                    .ToArray());
        var bindingsByEntity = bindingRows.ToDictionary(row => row.EntityId);
        var result = new Dictionary<Guid, MonitorEligibilityEntity>();
        foreach (var entity in entities) {
            if (!EntityKindRegistry.TryGet(entity.KindCode, out var kind)) {
                continue;
            }

            var identities = identitiesByEntity.GetValueOrDefault(entity.Id) ?? [];
            var binding = bindingsByEntity.GetValueOrDefault(entity.Id);
            PluginIdentityRoute? providerIdentity = null;
            if (binding is not null) {
                var boundIdentity = TryIdentity(binding.IdentityNamespace, binding.IdentityValue);
                if (boundIdentity is not null) {
                    providerIdentity = new PluginIdentityRoute(binding.PluginId, boundIdentity);
                }
            }

            result[entity.Id] = new MonitorEligibilityEntity(
                entity.Id,
                kind,
                entity.IsWanted,
                identities,
                providerIdentity);
        }

        return result;
    }

    public async Task<IReadOnlyList<Guid>> ListWantedChildIdsAsync(
        Guid parentEntityId, EntityKind childKind, CancellationToken cancellationToken) {
        var kindCode = childKind.ToCode();
        // IsWanted alone should imply fileless (the import bind clears the flag), but the file check
        // guards against a bind raced by the caller — an owned child is never a gap to chase.
        return await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == parentEntityId && row.KindCode == kindCode && row.IsWanted)
            .Where(row => !db.EntityFiles.Any(file => file.EntityId == row.Id && file.Role == EntityFileRole.Source))
            .OrderBy(row => row.SortOrder)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListChildIdsAsync(
        Guid parentEntityId, EntityKind childKind, CancellationToken cancellationToken) {
        var kindCode = childKind.ToCode();
        return await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == parentEntityId && row.KindCode == kindCode)
            .OrderBy(row => row.SortOrder)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Whether this Entity's canonical subtree owns source media. Containers and root wrappers often own
    /// files only through descendants (Movie → Video, Series → Season → Episode), so a direct-row check
    /// would falsely mark an on-disk item as requestable and create duplicate acquisition work.
    /// </summary>
    private async Task<bool> HasSourceFileAsync(Guid entityId, CancellationToken cancellationToken) {
        var subtreeIds = await hierarchy.ListSubtreeIdsAsync(entityId, cancellationToken);
        return subtreeIds.Count > 0 && await db.EntityFiles.AsNoTracking()
            .AnyAsync(file => subtreeIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source, cancellationToken);
    }

    private static ExternalIdentity? TryIdentity(string? identityNamespace, string? identityValue) {
        try {
            return new ExternalIdentity(identityNamespace ?? string.Empty, identityValue ?? string.Empty);
        } catch (ArgumentException) {
            return null;
        }
    }

    /// <summary>
    /// Title fallback, mirroring the apply cascade's scoping rules: a container grouping matches
    /// kind-wide (so a request binds to an already-scanned author/artist folder that has no provider ids
    /// yet), while a leaf only matches inside its given parent — a bare title is too weak a signal to
    /// claim a leaf globally.
    /// </summary>
    private async Task<EntityRow?> FindByTitleAsync(
        string kindCode, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken) {
        if (!matchTitleKindWide && parentEntityId is null) {
            return null;
        }

        var lowered = title.Trim().ToLower();
        return await db.Entities.FirstOrDefaultAsync(row =>
            row.KindCode == kindCode &&
            (matchTitleKindWide || row.ParentEntityId == parentEntityId) &&
            row.Title.ToLower() == lowered,
            cancellationToken);
    }

    private Task StampExternalIdAsync(
        Guid entityId,
        ExternalIdentity identity,
        CancellationToken cancellationToken) =>
        externalIdentities.WriteAsync(
            entityId,
            [new EntityExternalId(identity)],
            ExternalIdentityWriteMode.AddMissing,
            cancellationToken);
}
