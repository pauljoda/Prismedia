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
/// <param name="sourceOwnership">Bounded source-ownership projection for reviewed child batches.</param>
public sealed class WantedEntityWriter(
    PrismediaDbContext db,
    EntityMetadataApplyService apply,
    IEntityExternalIdentityStore externalIdentities,
    IEntityProviderIdentityStore providerIdentities,
    IPluginIdentityRouter identityRouter,
    IEntityHierarchyReader hierarchy,
    IEntitySourceOwnershipReader sourceOwnership,
    IEntityLifecycleMutationLease? lifecycle = null) : IWantedEntityWriter {
    private readonly IEntityLifecycleMutationLease lifecycleLease =
        lifecycle ?? new EfEntityLifecycleMutationLease(db, hierarchy);
    /// <summary>Wanted container kinds whose empty placeholders are pruned with their last child (from the request registry).</summary>
    private static readonly HashSet<string> ContainerKindCodes = RequestKindRegistry.All
        .Where(descriptor => descriptor.IsContainer)
        .Select(descriptor => descriptor.WantedEntityKind.ToCode())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public Task<WantedEntityResult> EnsureAsync(
        EntityKind kind,
        ExternalIdentity identity,
        string title,
        Guid? parentEntityId,
        bool matchTitleKindWide,
        CancellationToken cancellationToken) =>
        EnsureAsync(kind, identity, title, parentEntityId, matchTitleKindWide, cancellationToken,
            kind == EntityKind.Book ? BookRendition.Ebook : null);

    public async Task<WantedEntityResult> EnsureAsync(
        EntityKind kind, ExternalIdentity identity, string title, Guid? parentEntityId, bool matchTitleKindWide,
        CancellationToken cancellationToken, BookRendition? bookRendition) {
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
                    var hasRequestedRendition = await HasRequestedRenditionAsync(
                        current.Id, kind, bookRendition, leaseCancellationToken);

                    // A title-matched entity (e.g. a scanned author/artist folder with no provider ids yet)
                    // gains the provider id so every later lookup resolves it id-first.
                    await StampExternalIdAsync(current.Id, identity, leaseCancellationToken);

                    // Provider hydration can materialize a fileless Entity before an acquisition is
                    // selected. Reusing that shell for a request must promote it to Wanted so both the
                    // acquisition store and library projections recognize the pending work.
                    if (!hasRequestedRendition && !current.IsWanted) {
                        current.IsWanted = true;
                        current.UpdatedAt = now;
                    }

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
                    result = new WantedEntityResult(
                        current.Id, Created: false, hasFile, hasRequestedRendition);
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
            created = new WantedEntityResult(
                entity.Id, Created: true, HasFile: false, RequestedRenditionOwned: false);
        }

        if (parentEntityId is { } parentId) {
            await ExecuteIfLifecycleMutableAsync(parentId, CreateAsync, cancellationToken);
        } else {
            await CreateAsync(cancellationToken);
        }
        return created ?? throw LifecycleConflict();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WantedEntityResult>> EnsureChildrenAsync(
        Guid parentEntityId,
        IReadOnlyList<WantedEntityEnsureRequest> requests,
        CancellationToken cancellationToken) {
        if (requests.Count == 0) {
            return [];
        }

        var kind = requests[0].Kind;
        if (requests.Any(request => request.Kind != kind)) {
            throw new ArgumentException("A reviewed child batch must contain one Entity kind.", nameof(requests));
        }
        if (requests.Select(request => request.Identity).Distinct().Count() != requests.Count) {
            throw new ArgumentException("A reviewed child batch cannot contain duplicate external identities.", nameof(requests));
        }
        if (requests
                .Where(request => request.PreferredEntityId is not null)
                .Select(request => request.PreferredEntityId)
                .Distinct()
                .Count()
            != requests.Count(request => request.PreferredEntityId is not null)) {
            throw new ArgumentException("A reviewed child batch cannot target the same local Entity twice.", nameof(requests));
        }

        IReadOnlyList<WantedEntityResult>? results = null;
        await ExecuteIfLifecycleMutableAsync(
            parentEntityId,
            async leaseCancellationToken => {
                var kindCode = kind.ToCode();
                var identities = requests.Select(request => request.Identity).ToHashSet();
                var namespaces = identities.Select(identity => identity.Namespace).Distinct().ToArray();
                var values = identities.Select(identity => identity.Value).Distinct().ToArray();
                var identityRows = await db.EntityExternalIds.AsNoTracking()
                    .Where(row => namespaces.Contains(row.Provider.Trim().ToLower())
                        && values.Contains(row.Value.Trim()))
                    .ToArrayAsync(leaseCancellationToken);
                var exactIdentityRows = identityRows
                    .Select(row => (Row: row, Identity: TryIdentity(row.Provider, row.Value)))
                    .Where(pair => pair.Identity is not null && identities.Contains(pair.Identity))
                    .ToArray();
                var candidateIds = exactIdentityRows.Select(pair => pair.Row.EntityId).Distinct().ToArray();
                var identityEntities = await db.Entities
                    .Where(row => candidateIds.Contains(row.Id)
                        && row.KindCode == kindCode
                        && row.ParentEntityId == parentEntityId)
                    .ToDictionaryAsync(row => row.Id, leaseCancellationToken);
                var preferredIds = requests
                    .Where(request => request.PreferredEntityId is not null)
                    .Select(request => request.PreferredEntityId!.Value)
                    .Distinct()
                    .ToArray();
                var preferredEntities = await db.Entities
                    .Where(row => preferredIds.Contains(row.Id)
                        && row.KindCode == kindCode
                        && row.ParentEntityId == parentEntityId)
                    .ToDictionaryAsync(row => row.Id, leaseCancellationToken);

                var entitiesByIdentity = exactIdentityRows
                    .Where(pair => identityEntities.ContainsKey(pair.Row.EntityId))
                    .GroupBy(pair => pair.Identity!)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(pair => pair.Row.EntityId).Distinct().Order().ToArray());
                foreach (var (identity, entityIds) in entitiesByIdentity) {
                    if (entityIds.Length <= 1) {
                        continue;
                    }

                    var resolution = await externalIdentities.ResolveAsync(
                        kind,
                        [identity],
                        parentEntityId,
                        leaseCancellationToken);
                    throw new ExternalIdentityAmbiguityException(kind, resolution);
                }

                var requestedTitles = requests
                    .Select(request => request.Title.Trim().ToLower())
                    .Distinct()
                    .ToArray();
                var titleCandidates = await db.Entities
                    .Where(row => row.KindCode == kindCode
                        && row.ParentEntityId == parentEntityId
                        && requestedTitles.Contains(row.Title.ToLower()))
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(leaseCancellationToken);
                var titleCandidatesByTitle = titleCandidates
                    .GroupBy(row => row.Title.Trim().ToLower())
                    .ToDictionary(group => group.Key, group => new Queue<EntityRow>(group));
                var allocatedTitleMatches = new HashSet<Guid>();
                var now = DateTimeOffset.UtcNow;
                var materialized = new List<(WantedEntityEnsureRequest Request, EntityRow Entity, bool Created)>(requests.Count);

                foreach (var request in requests) {
                    EntityRow? entity = null;
                    entitiesByIdentity.TryGetValue(request.Identity, out var identityEntityIds);
                    if (request.PreferredEntityId is { } preferredId
                        && preferredEntities.TryGetValue(preferredId, out var preferredEntity)) {
                        if (identityEntityIds is { Length: > 0 }
                            && identityEntityIds.Any(entityId => entityId != preferredId)) {
                            var resolution = await externalIdentities.ResolveAsync(
                                kind,
                                [request.Identity],
                                parentEntityId,
                                leaseCancellationToken);
                            throw new ExternalIdentityAmbiguityException(kind, resolution);
                        }

                        entity = preferredEntity;
                        allocatedTitleMatches.Add(entity.Id);
                    } else if (identityEntityIds is { Length: 1 }) {
                        entity = identityEntities[identityEntityIds[0]];
                        allocatedTitleMatches.Add(entity.Id);
                    }

                    if (entity is null
                        && titleCandidatesByTitle.TryGetValue(request.Title.Trim().ToLower(), out var candidates)) {
                        while (candidates.Count > 0 && entity is null) {
                            var candidate = candidates.Dequeue();
                            if (allocatedTitleMatches.Add(candidate.Id)) {
                                entity = candidate;
                            }
                        }
                    }

                    if (entity is not null) {
                        // Only a title fallback needs a new identity association. Exact identity matches
                        // already carry the row used above; AddMissing preserves any other value already
                        // claimed by this Entity for the same namespace.
                        if (!entitiesByIdentity.ContainsKey(request.Identity)) {
                            await StampExternalIdAsync(entity.Id, request.Identity, leaseCancellationToken);
                        }
                        materialized.Add((request, entity, Created: false));
                        continue;
                    }

                    entity = new EntityRow {
                        Id = Guid.NewGuid(),
                        KindCode = kindCode,
                        Title = request.Title.Trim(),
                        ParentEntityId = parentEntityId,
                        IsWanted = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.Entities.Add(entity);
                    AddDetailRowFor(kind, entity.Id);
                    db.EntityExternalIds.Add(new EntityExternalIdRow {
                        Id = Guid.NewGuid(),
                        EntityId = entity.Id,
                        Provider = request.Identity.Namespace,
                        Value = request.Identity.Value,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    materialized.Add((request, entity, Created: true));
                }

                await db.SaveChangesAsync(leaseCancellationToken);
                var existingIds = materialized
                    .Where(item => !item.Created)
                    .Select(item => item.Entity.Id)
                    .Distinct()
                    .ToArray();
                var sourceBackedIds = await sourceOwnership.ResolveAsync(
                    existingIds,
                    leaseCancellationToken);
                var resolved = new List<WantedEntityResult>(materialized.Count);
                var promotedWantedEntity = false;
                foreach (var item in materialized) {
                    if (item.Created) {
                        resolved.Add(new WantedEntityResult(
                            item.Entity.Id,
                            Created: true,
                            HasFile: false,
                            RequestedRenditionOwned: false));
                        continue;
                    }

                    var hasFile = sourceBackedIds.Contains(item.Entity.Id);
                    var hasRequestedRendition = item.Request.Kind == EntityKind.Book
                        ? await HasRequestedRenditionAsync(
                            item.Entity.Id,
                            item.Request.Kind,
                            item.Request.BookRendition,
                            leaseCancellationToken)
                        : hasFile;
                    // Structural/provider matching can select an existing metadata-only child. It becomes
                    // a real Wanted child at the moment this reviewed batch requests it; otherwise the
                    // acquisition guard rejects it and its parent detail view filters it out.
                    if (!hasRequestedRendition && !item.Entity.IsWanted) {
                        item.Entity.IsWanted = true;
                        item.Entity.UpdatedAt = now;
                        promotedWantedEntity = true;
                    }
                    resolved.Add(new WantedEntityResult(
                        item.Entity.Id,
                        Created: false,
                        hasFile,
                        hasRequestedRendition));
                }
                if (promotedWantedEntity) {
                    await db.SaveChangesAsync(leaseCancellationToken);
                }

                results = resolved;
            },
            cancellationToken);
        return results ?? throw LifecycleConflict();
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

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> BindProviderIdentitiesAsync(
        IReadOnlyDictionary<Guid, PluginIdentityRoute> routes,
        CancellationToken cancellationToken) {
        if (routes.Count == 0) {
            return new HashSet<Guid>();
        }

        var entityIds = routes.Keys.ToArray();
        var entities = await db.Entities.AsNoTracking()
            .Where(entity => entityIds.Contains(entity.Id))
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);
        var externalRows = await db.EntityExternalIds.AsNoTracking()
            .Where(row => entityIds.Contains(row.EntityId))
            .ToArrayAsync(cancellationToken);
        var ownedIdentities = externalRows
            .Select(row => (row.EntityId, Identity: TryIdentity(row.Provider, row.Value)))
            .Where(value => value.Identity is not null)
            .Select(value => (value.EntityId, value.Identity!))
            .ToHashSet();

        var validEntityIds = new HashSet<Guid>();
        foreach (var kindGroup in routes
            .Where(route => entities.ContainsKey(route.Key))
            .GroupBy(route => entities[route.Key].KindCode)) {
            var requestedRoutes = kindGroup.ToArray();
            var supportedRoutes = await identityRouter.ResolveAsync(
                kindGroup.Key,
                IdentifyAction.LookupId,
                requestedRoutes.Select(route => route.Value.Identity).Distinct().ToArray(),
                cancellationToken);
            foreach (var (entityId, requestedRoute) in requestedRoutes) {
                var supported = supportedRoutes.Any(candidate =>
                    candidate.Identity == requestedRoute.Identity
                    && string.Equals(
                        candidate.PluginId,
                        requestedRoute.PluginId,
                        StringComparison.OrdinalIgnoreCase));
                if (supported && ownedIdentities.Contains((entityId, requestedRoute.Identity))) {
                    validEntityIds.Add(entityId);
                }
            }
        }

        if (validEntityIds.Count == 0) {
            return validEntityIds;
        }

        var existingRows = await db.EntityProviderIdentities
            .Where(row => validEntityIds.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var entityId in validEntityIds) {
            var route = routes[entityId];
            var pluginId = route.PluginId.Trim().ToLowerInvariant();
            if (!existingRows.TryGetValue(entityId, out var row)) {
                db.EntityProviderIdentities.Add(new EntityProviderIdentityRow {
                    EntityId = entityId,
                    PluginId = pluginId,
                    IdentityNamespace = route.Identity.Namespace,
                    IdentityValue = route.Identity.Value,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            if (row.PluginId == pluginId
                && row.IdentityNamespace == route.Identity.Namespace
                && row.IdentityValue == route.Identity.Value) {
                continue;
            }

            row.PluginId = pluginId;
            row.IdentityNamespace = route.Identity.Namespace;
            row.IdentityValue = route.Identity.Value;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return validEntityIds;
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
    /// Applies reviewed metadata without downloading artwork on the request boundary. Remote image URLs
    /// are persisted as ordinary artwork-role references so entity grids can render the exact images the
    /// review already showed; a later metadata hydration atomically replaces each URL with its cached path.
    /// </summary>
    public async Task ApplyProposalWithDeferredArtworkAsync(
        Guid entityId,
        Contracts.Plugins.EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        await ExecuteIfLifecycleMutableAsync(
            entityId,
            async leaseCancellationToken => {
                await TrackDeferredArtworkReferencesAsync(entityId, proposal, leaseCancellationToken);
                var metadataOnly = WithoutArtwork(proposal);
                var fields = ProposalApplySelection.SelectAllPresentFields(metadataOnly);
                await apply.ApplyAsync(
                    entityId,
                    metadataOnly,
                    fields,
                    selectedImages: null,
                    leaseCancellationToken);
            },
            cancellationToken);
    }

    private async Task TrackDeferredArtworkReferencesAsync(
        Guid rootEntityId,
        Contracts.Plugins.EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        var references = EnumerateTargetedProposals(rootEntityId, proposal)
            .SelectMany(target => (ProposalApplySelection.SelectDefaultImages(target.Proposal)
                    ?? new Dictionary<string, string?>())
                .Where(image => image.Value is not null)
                .Select(image => new {
                    target.EntityId,
                    Role = ImageKindRoleResolver.RoleFor(image.Key),
                    Url = image.Value!
                }))
            .GroupBy(reference => (reference.EntityId, reference.Role))
            .Select(group => group.First())
            .ToArray();
        if (references.Length == 0) {
            return;
        }

        var entityIds = references.Select(reference => reference.EntityId).Distinct().ToArray();
        var roles = references.Select(reference => reference.Role).Distinct().ToArray();
        var existing = await db.EntityFiles
            .Where(file => entityIds.Contains(file.EntityId) && roles.Contains(file.Role))
            .ToArrayAsync(cancellationToken);
        var filesByRole = existing.ToDictionary(file => (file.EntityId, file.Role));
        var now = DateTimeOffset.UtcNow;

        foreach (var reference in references) {
            if (!filesByRole.TryGetValue((reference.EntityId, reference.Role), out var file)) {
                file = new EntityFileRow {
                    Id = Guid.NewGuid(),
                    EntityId = reference.EntityId,
                    Role = reference.Role,
                    Path = reference.Url,
                    Source = FileSourceKind.Custom.ToCode(),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.EntityFiles.Add(file);
                filesByRole[(reference.EntityId, reference.Role)] = file;
                continue;
            }

            // Never replace an already-localized or user-uploaded asset with a remote reference.
            if (Uri.TryCreate(file.Path, UriKind.Absolute, out var current)
                && (string.Equals(current.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))) {
                file.Path = reference.Url;
                file.Source = FileSourceKind.Custom.ToCode();
                file.UpdatedAt = now;
            }
        }
    }

    private static IEnumerable<(Guid EntityId, Contracts.Plugins.EntityMetadataProposal Proposal)>
        EnumerateTargetedProposals(
            Guid rootEntityId,
            Contracts.Plugins.EntityMetadataProposal proposal) {
        yield return (rootEntityId, proposal);
        foreach (var child in proposal.Children.Concat(proposal.Relationships ?? [])) {
            if (child.TargetEntityId is not { } childEntityId) {
                continue;
            }
            foreach (var target in EnumerateTargetedProposals(childEntityId, child)) {
                yield return target;
            }
        }
    }

    private static Contracts.Plugins.EntityMetadataProposal WithoutArtwork(
        Contracts.Plugins.EntityMetadataProposal proposal) =>
        proposal with {
            Images = [],
            Children = proposal.Children.Select(WithoutArtwork).ToArray(),
            Relationships = (proposal.Relationships ?? []).Select(WithoutArtwork).ToArray()
        };

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
        bool? hasEbookSource = null;
        bool? hasAudiobookSource = null;
        if (entity.KindCode == EntityKindRegistry.Book.Code) {
            hasEbookSource = await HasRequestedRenditionAsync(
                entityId, EntityKind.Book, BookRendition.Ebook, cancellationToken);
            hasAudiobookSource = await HasRequestedRenditionAsync(
                entityId, EntityKind.Book, BookRendition.Audiobook, cancellationToken);
        }
        return new MonitorableEntity(
            entity.Id, entity.KindCode.DecodeAs<EntityKind>(), entity.Title, identities, hasSource,
            entity.ParentEntityId, positions, providerIdentity, hasEbookSource, hasAudiobookSource);
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

    /// <summary>
    /// Book text and spoken audio are independent ownership axes. Audio ownership is represented by a
    /// source-backed AudioTrack descendant; every other source-backed node in the Book subtree is text/page
    /// ownership. Non-book kinds retain the established generic subtree ownership rule.
    /// </summary>
    private async Task<bool> HasRequestedRenditionAsync(
        Guid entityId,
        EntityKind kind,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) {
        if (kind != EntityKind.Book) {
            return await HasSourceFileAsync(entityId, cancellationToken);
        }

        var subtreeIds = await hierarchy.ListSubtreeIdsAsync(entityId, cancellationToken);
        if (subtreeIds.Count == 0) {
            return false;
        }

        var audioTrackCode = EntityKindRegistry.AudioTrack.Code;
        var bookCode = EntityKindRegistry.Book.Code;
        var ownsAudio = bookRendition == BookRendition.Audiobook;
        return await (
            from file in db.EntityFiles.AsNoTracking()
            join entity in db.Entities.AsNoTracking() on file.EntityId equals entity.Id
            join bookDetail in db.BookDetails.AsNoTracking() on entity.Id equals bookDetail.EntityId into bookDetails
            from bookDetail in bookDetails.DefaultIfEmpty()
            where subtreeIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source
            where ownsAudio
                ? entity.KindCode == audioTrackCode
                : entity.KindCode != audioTrackCode
                    && (entity.KindCode != bookCode || bookDetail == null || bookDetail.Format != BookFormat.Audio)
            select file.Id).AnyAsync(cancellationToken);
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
