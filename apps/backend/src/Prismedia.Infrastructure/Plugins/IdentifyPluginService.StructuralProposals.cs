using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Structural-proposal construction, child matching/merging, and entity snapshot helpers
/// for <see cref="IdentifyPluginService"/>.
/// </summary>
public sealed partial class IdentifyPluginService {
    private async Task<EntityMetadataProposal> BuildStructuralProposalAsync(
        EntityRow entity,
        EntityMetadataProposal providerProposal,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IReadOnlyList<IdentifyEntitySnapshot> ancestorPath,
        bool includeNsfw,
        HashSet<Guid> visited,
        CancellationToken cancellationToken,
        bool cascadeChildren = true,
        IIdentifyCascadeSink? sink = null,
        bool streamRootProgress = true,
        bool hydrateRelationships = true) {
        // Bind the children the provider already returned in its own proposal to local entities.
        var boundProviderProposal = await BindLocalStructuralTargetsAsync(providerProposal, entity.Id, cancellationToken);
        var titledProposal = EnsureMeaningfulTitle(boundProviderProposal, entity.Title);

        // Relationship proposals can arrive either in the dedicated Relationships collection (newer
        // plugins) or as relationship-kind child nodes (older plugin shapes). Normalize them into the
        // relationship lane so the app can stream their hydration exactly like structural children.
        var baseRelationships = MergeRelationshipProposals(
            EntityMetadataProposalTraversal.Relationships(titledProposal),
            RelationshipChildren(titledProposal));

        // Base children for every streamed root: any provider child that has no local entity yet (e.g.
        // an unscanned season) so it can be materialized. Crucially we exclude provider children already
        // bound to a LOCAL entity here: those only appear once the cascade has fully resolved them below,
        // so a child being present in the streamed proposal reliably means it is done — which is what the
        // review grid keys off. Without this, a provider that returns its whole tree up front (e.g.
        // MangaDex volumes, a TMDB season) would show every child as "matched" instantly while the cascade
        // was still resolving their own children, with no per-child progress.
        var baseChildren = (titledProposal.Children ?? [])
            .Where(child =>
                !EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind) &&
                child.TargetEntityId is null)
            .ToArray();

        // Builds the root proposal shape from the children and relationships resolved so far — used both
        // for the final return and for each partial-root the streaming sink publishes. Resolved children
        // that an unbound provider container carries as shells (a volume's chapters when the book was
        // scanned flat) are swapped into the container as they resolve, so the reviewed tree shows the
        // provider's structure with the local matches nested where they belong.
        EntityMetadataProposal Root(
            IReadOnlyList<EntityMetadataProposal> structural,
            IReadOnlyList<EntityMetadataProposal> relationships) {
            var (containers, remaining) = AdoptResolvedChildrenIntoContainers(baseChildren, structural);
            return titledProposal with {
                TargetKind = entity.KindCode.DecodeAs<ProposalKind>(),
                TargetEntityId = entity.Id,
                Children = MergeStructuralChildren(containers, remaining),
                Relationships = MergeRelationshipProposals(baseRelationships, relationships)
            };
        }

        var hydratedRelationships = new List<EntityMetadataProposal>();

        // Seed the sink with the parent (no local children or hydrated relationships yet) so the queue
        // item shows it immediately with a stable ProposalId; relationships and children stream in one at
        // a time as the cascade resolves each.
        if (sink is not null && streamRootProgress) {
            await SafeFlushAsync(sink, Root([], hydratedRelationships), cancellationToken);
        }

        // Hydrate related people/studios/tags incrementally before walking structural children. This
        // mirrors the child cascade: the review opens with lightweight relationship shells, then each
        // actor/studio/tag card gains provider detail as it resolves instead of blocking the first view.
        if (cascadeChildren && hydrateRelationships && baseRelationships.Count > 0) {
            foreach (var relationship in baseRelationships) {
                if (sink is not null && !await sink.IsActiveAsync(cancellationToken)) {
                    break;
                }

                var hydrated = await HydrateRelationshipProposalAsync(
                    relationship,
                    descriptor,
                    auth,
                    includeNsfw,
                    cancellationToken);
                hydratedRelationships.Add(hydrated);

                if (sink is not null) {
                    if (streamRootProgress) {
                        await SafeFlushAsync(sink, Root([], hydratedRelationships), cancellationToken);
                    } else {
                        await SafeProgressAsync(sink, cancellationToken);
                    }
                }
            }
        }

        // Walk the local structural children and identify each one with this entity's context threaded
        // down (its freshly-identified external ids as an ancestor, plus the child's structural
        // position such as a season number). This is the recursive full-tree cascade: a series fills
        // its seasons and episodes, a book fills its volumes and chapters, etc. Only identify-container
        // kinds cascade — a leaf-content kind (a movie wrapping a single video) is one work and never
        // walks into its own media file. Child recursions never flush (sink: null) so only the root
        // streams; each child's full subtree is resolved before it is merged into the root.
        var structuralChildren = new List<EntityMetadataProposal>();
        if (cascadeChildren && EntityKindRegistry.EnumeratesIdentifyChildren(entity.KindCode)) {
            var persistedChildren = await LoadStructuralChildrenAsync(entity.Id, cancellationToken);
            var existingChildren = persistedChildren
                .Where(child => child.IsIdentifyEligible)
                .ToArray();
            var eligibleChildIds = existingChildren
                .Select(child => child.Entity.Id)
                .ToHashSet();
            var providerStructuralChildren = EntityMetadataProposalTraversal.StructuralChildren(boundProviderProposal)
                .Where(child => child.TargetEntityId is not { } localId || eligibleChildIds.Contains(localId))
                .ToArray();
            var cautiousStructuralMatching = ShouldUseCautiousStructuralMatching(existingChildren, providerStructuralChildren);
            var usedProviderIndexes = new HashSet<int>();
            foreach (var child in existingChildren) {
                // Sanity-check before each child that the streaming destination is still live. If the
                // user removed this item from the queue (or a newer search superseded it), the sink
                // reports inactive and we drop the rest of the walk — otherwise an orphaned background
                // cascade keeps resolving children and re-populates the removed item, popping it back
                // into the queue and conflicting with the next time it is queued.
                if (sink is not null && !await sink.IsActiveAsync(cancellationToken)) {
                    break;
                }

                if (!SupportsKind(descriptor.Manifest, child.Entity.KindCode)) {
                    continue;
                }

                var providerChild = StructuralChildMatcher.FindProviderChild(
                    ToMatchInput(child),
                    providerStructuralChildren,
                    usedProviderIndexes,
                    cautiousStructuralMatching);
                if (providerChild is not null) {
                    structuralChildren.Add(await HydrateMatchedProviderChildAsync(
                        child, providerChild, descriptor, auth, ancestorPath, includeNsfw, visited, cancellationToken, sink));
                } else if (cautiousStructuralMatching) {
                    structuralChildren.Add(UnmatchedLocalStructuralChild(child, descriptor.Manifest.Name));
                } else {
                    var childResponse = await IdentifyEntityWithStructuralContextAsync(
                        child.Entity, descriptor, auth, query: null, ancestors: ancestorPath,
                        parentSortOrder: child.SortOrder, includeNsfw, visited, cancellationToken,
                        cascadeChildren: true, sink: sink, streamRootProgress: false);
                    if (childResponse.Ok && childResponse.Result?.Patch is not null) {
                        structuralChildren.Add(EnsureStructuralPositions(childResponse.Result, child));
                    } else {
                        continue;
                    }
                }

                if (sink is not null) {
                    if (streamRootProgress) {
                        await SafeFlushAsync(sink, Root(structuralChildren, hydratedRelationships), cancellationToken);
                    } else {
                        await SafeProgressAsync(sink, cancellationToken);
                    }
                }
            }
        }

        var relocatedChildren = await RelocateUnmatchedChildrenIntoProviderContainersAsync(
            structuralChildren,
            baseChildren,
            descriptor,
            auth,
            ancestorPath,
            includeNsfw,
            cancellationToken);
        return Root(relocatedChildren, hydratedRelationships);
    }

    /// <summary>
    /// When a local child is scanned under one structural container but the provider catalogs it under
    /// a sibling container (Bluey's long special “The Sign” is season 0 on TMDB, while a file may sit in
    /// season 3), hydrate the provider's unbound sibling containers and move the local match into the
    /// provider's proposed location. The later apply walk will materialize that provider container and
    /// adopt the existing local entity beneath it, rather than showing a false “matched” placeholder in
    /// the original container.
    /// </summary>
    private async Task<IReadOnlyList<EntityMetadataProposal>> RelocateUnmatchedChildrenIntoProviderContainersAsync(
        IReadOnlyList<EntityMetadataProposal> resolvedChildren,
        IReadOnlyList<EntityMetadataProposal> baseChildren,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IReadOnlyList<IdentifyEntitySnapshot> ancestorPath,
        bool includeNsfw,
        CancellationToken cancellationToken) {
        var unmatched = CollectLocalUnmatchedDescendants(resolvedChildren).ToArray();
        var candidateContainers = baseChildren
            .Where(child => child.TargetEntityId is null &&
                IsStructuralContainerKind(child.TargetKind) &&
                !EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind))
            .ToArray();
        if (unmatched.Length == 0 || candidateContainers.Length == 0) {
            return resolvedChildren;
        }

        var relocatedById = new Dictionary<Guid, EntityMetadataProposal>();
        var containers = new List<EntityMetadataProposal>();
        foreach (var container in candidateContainers) {
            var hydrated = await HydrateUnboundProviderContainerAsync(
                container, descriptor, auth, ancestorPath, includeNsfw, cancellationToken);
            if (hydrated?.Patch is null) {
                continue;
            }

            var providerChildren = EntityMetadataProposalTraversal.StructuralChildren(hydrated);
            if (providerChildren.Count == 0) {
                continue;
            }

            var usedProviderIndexes = new HashSet<int>();
            var matchedChildren = new List<EntityMetadataProposal>();
            foreach (var local in unmatched) {
                if (relocatedById.ContainsKey(local.EntityId)) {
                    continue;
                }

                var providerChild = StructuralChildMatcher.FindProviderChild(
                    new StructuralLocalChild(local.EntityId, local.KindCode, local.Title, local.SortOrder),
                    providerChildren,
                    usedProviderIndexes,
                    cautious: true);
                if (providerChild is null) {
                    continue;
                }

                var boundChild = providerChild with { TargetEntityId = local.EntityId };
                relocatedById[local.EntityId] = boundChild;
                matchedChildren.Add(boundChild);
            }

            if (matchedChildren.Count > 0) {
                containers.Add(hydrated with { Children = matchedChildren });
            }
        }

        if (relocatedById.Count == 0) {
            return resolvedChildren;
        }

        return RemoveRelocatedLocalUnmatched(resolvedChildren, relocatedById.Keys.ToHashSet())
            .Concat(containers)
            .ToArray();
    }

    private async Task<EntityMetadataProposal?> HydrateUnboundProviderContainerAsync(
        EntityMetadataProposal container,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IReadOnlyList<IdentifyEntitySnapshot> ancestorPath,
        bool includeNsfw,
        CancellationToken cancellationToken) {
        var kind = container.TargetKind.ToEntityKind();
        var request = new IdentifyPluginRequest(
            ProtocolVersion: PluginProtocol.CurrentVersion,
            Action: IdentifyAction.LookupId,
            Auth: auth,
            Entity: new IdentifyEntitySnapshot(
                Guid.Empty,
                kind,
                container.Patch.Title ?? kind.ToCode(),
                container.Patch.ExternalIds,
                container.Patch.Urls),
            Query: new IdentifyQuery(null, null, null),
            Hints: new IdentifyMatchHints(container.Patch.ExternalIds, container.Patch.Urls, container.Patch.Title, null),
            StructuralContext: new IdentifyStructuralContext(ancestorPath, container.Patch.Positions),
            IncludeNsfw: includeNsfw,
            IncludeRelationshipDetails: false,
            IncludeStructuralChildren: true);

        var response = await _runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
        return response.Ok && response.Result?.Patch is not null ? response.Result : null;
    }

    private async Task<EntityMetadataProposal> HydrateRelationshipProposalAsync(
        EntityMetadataProposal relationship,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        bool includeNsfw,
        CancellationToken cancellationToken) {
        if (!SupportsKind(descriptor.Manifest, relationship.TargetKind.ToEntityKind().ToCode())) {
            return relationship;
        }

        var patch = relationship.Patch;
        var externalIds = patch.ExternalIds ?? new Dictionary<string, string>();
        var urls = patch.Urls ?? [];
        var title = patch.Title?.Trim() ?? relationship.TargetKind.ToEntityKind().ToCode();
        var action = externalIds.Count > 0
            ? IdentifyAction.LookupId
            : urls.Count > 0
                ? IdentifyAction.LookupUrl
                : IdentifyAction.Search;
        var query = action switch {
            IdentifyAction.LookupId => new IdentifyQuery(null, null, externalIds),
            IdentifyAction.LookupUrl => new IdentifyQuery(null, urls.FirstOrDefault(), null),
            _ => new IdentifyQuery(title, null, null)
        };
        var request = new IdentifyPluginRequest(
            ProtocolVersion: PluginProtocol.CurrentVersion,
            Action: action,
            Auth: auth,
            Entity: new IdentifyEntitySnapshot(
                Guid.Empty,
                relationship.TargetKind.ToEntityKind(),
                title,
                externalIds,
                urls),
            Query: query,
            Hints: new IdentifyMatchHints(externalIds, urls, title, null),
            StructuralContext: null,
            IncludeNsfw: includeNsfw,
            IncludeRelationshipDetails: false,
            IncludeStructuralChildren: false);

        try {
            var response = await _runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
            if (response.Ok && response.Result?.Patch is not null) {
                return response.Result with {
                    TargetEntityId = relationship.TargetEntityId,
                    // Preserve the shell's id so selection state and card identity remain stable while
                    // the hydrated relationship replaces the lightweight base node in-place.
                    ProposalId = relationship.ProposalId
                };
            }
        } catch (OperationCanceledException) {
            throw;
        } catch {
            // Relationship hydration is best-effort: keep the base shell so the root proposal still
            // applies credits/studio/tags even if one related entity detail lookup fails.
        }

        return relationship;
    }

    private static IReadOnlyList<EntityMetadataProposal> RelationshipChildren(EntityMetadataProposal proposal) =>
        (proposal.Children ?? [])
            .Where(child => EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind))
            .ToArray();

    private static IReadOnlyList<EntityMetadataProposal> MergeRelationshipProposals(
        IReadOnlyList<EntityMetadataProposal> baseRelationships,
        IReadOnlyList<EntityMetadataProposal> hydratedRelationships) {
        if (baseRelationships.Count == 0) {
            return NormalizeRelationships(hydratedRelationships);
        }

        if (hydratedRelationships.Count == 0) {
            return NormalizeRelationships(baseRelationships);
        }

        var merged = new List<EntityMetadataProposal>(baseRelationships);
        foreach (var hydrated in hydratedRelationships) {
            var index = merged.FindIndex(existing => RelationshipKey(existing) == RelationshipKey(hydrated));
            if (index >= 0) {
                merged[index] = hydrated;
            } else {
                merged.Add(hydrated);
            }
        }

        return NormalizeRelationships(merged);
    }

    private static IReadOnlyList<EntityMetadataProposal> NormalizeRelationships(
        IReadOnlyList<EntityMetadataProposal> relationships) =>
        relationships
            .GroupBy(RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private static string RelationshipKey(EntityMetadataProposal relationship) {
        if (!string.IsNullOrWhiteSpace(relationship.ProposalId)) {
            return $"proposal:{relationship.ProposalId}";
        }

        var externalId = relationship.Patch.ExternalIds
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(pair => !string.IsNullOrWhiteSpace(pair.Value));
        if (!string.IsNullOrWhiteSpace(externalId.Key)) {
            return $"external:{externalId.Key}:{externalId.Value}";
        }

        return $"title:{relationship.TargetKind}:{relationship.Patch.Title?.Trim()}";
    }

    private static IEnumerable<RelocatableLocalChild> CollectLocalUnmatchedDescendants(
        IEnumerable<EntityMetadataProposal> proposals) {
        foreach (var proposal in proposals) {
            if (IsLocalUnmatchedProposal(proposal) && proposal.TargetEntityId is { } targetId) {
                yield return new RelocatableLocalChild(
                    targetId,
                    proposal.TargetKind.ToEntityKind().ToCode(),
                    proposal.Patch.Title ?? string.Empty,
                    StructuralSortOrder(proposal));
            }

            foreach (var child in CollectLocalUnmatchedDescendants(EntityMetadataProposalTraversal.StructuralChildren(proposal))) {
                yield return child;
            }
        }
    }

    private static IReadOnlyList<EntityMetadataProposal> RemoveRelocatedLocalUnmatched(
        IReadOnlyList<EntityMetadataProposal> proposals,
        ISet<Guid> relocatedIds) =>
        proposals
            .Where(proposal => !(IsLocalUnmatchedProposal(proposal) &&
                proposal.TargetEntityId is { } targetId && relocatedIds.Contains(targetId)))
            .Select(proposal => proposal with {
                Children = RemoveRelocatedLocalUnmatched(
                    EntityMetadataProposalTraversal.StructuralChildren(proposal),
                    relocatedIds)
            })
            .ToArray();

    /// <summary>Publishes a partial root to the cascade sink, swallowing failures so streaming never aborts the cascade.</summary>
    private static async Task SafeFlushAsync(IIdentifyCascadeSink sink, EntityMetadataProposal partialRoot, CancellationToken cancellationToken) {
        try {
            await sink.OnEntityResolvedAsync(partialRoot, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch {
            // Best-effort streaming; the final proposal is still returned and persisted at job completion.
        }
    }

    /// <summary>Publishes a progress heartbeat without changing the streamed root proposal.</summary>
    private static async Task SafeProgressAsync(IIdentifyCascadeSink sink, CancellationToken cancellationToken) {
        try {
            await sink.OnProgressAsync(cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch {
            // Best-effort heartbeat; metadata resolution should continue if a progress sink is flaky.
        }
    }

    /// <summary>
    /// Hydrates a provider-returned child shell against the local entity. If the local child has its
    /// own supported structural descendants that the provider shell did not fully cover, re-identify
    /// the child so the cascade fills them in; otherwise the provider's version is already complete.
    /// </summary>
    private async Task<EntityMetadataProposal> HydrateMatchedProviderChildAsync(
        StructuralChild child,
        EntityMetadataProposal providerChild,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IReadOnlyList<IdentifyEntitySnapshot> ancestorPath,
        bool includeNsfw,
        HashSet<Guid> visited,
        CancellationToken cancellationToken,
        IIdentifyCascadeSink? sink) {
        if (!await HasSupportedStructuralChildrenAsync(child.Entity.Id, descriptor.Manifest, cancellationToken)) {
            return providerChild with { TargetEntityId = child.Entity.Id };
        }

        var providerStructuralChildren = EntityMetadataProposalTraversal.StructuralChildren(providerChild);
        if (providerStructuralChildren.Count > 0 &&
            !await HasMissingSupportedStructuralChildrenAsync(
                child.Entity.Id, providerStructuralChildren, descriptor.Manifest, cancellationToken)) {
            return providerChild with { TargetEntityId = child.Entity.Id };
        }

        var childResponse = await IdentifyEntityWithStructuralContextAsync(
            child.Entity, descriptor, auth, query: null, ancestors: ancestorPath,
            parentSortOrder: child.SortOrder, includeNsfw, visited, cancellationToken,
            sink: sink,
            streamRootProgress: false);
        return childResponse.Ok && childResponse.Result?.Patch is not null
            ? EnsureStructuralPositions(childResponse.Result, child) with { TargetEntityId = child.Entity.Id }
            : providerChild with { TargetEntityId = child.Entity.Id };
    }

    private async Task<bool> HasSupportedStructuralChildrenAsync(
        Guid parentEntityId,
        PluginManifest manifest,
        CancellationToken cancellationToken) {
        var localChildren = await LoadStructuralChildrenAsync(parentEntityId, cancellationToken);
        return localChildren
            .Where(child => child.IsIdentifyEligible)
            .Any(child => manifest.Supports.Any(support =>
                support.EntityKind.TryDecodeAs<ProposalKind>(out var supportKind) &&
                IsCompatibleStructuralKind(child.Entity.KindCode, supportKind)));
    }

    private async Task<bool> HasMissingSupportedStructuralChildrenAsync(
        Guid parentEntityId,
        IReadOnlyList<EntityMetadataProposal> providerChildren,
        PluginManifest manifest,
        CancellationToken cancellationToken) {
        var localChildren = await LoadStructuralChildrenAsync(parentEntityId, cancellationToken);
        return localChildren
            .Where(child => child.IsIdentifyEligible)
            .Where(child => manifest.Supports.Any(support =>
                support.EntityKind.TryDecodeAs<ProposalKind>(out var supportKind) &&
                IsCompatibleStructuralKind(child.Entity.KindCode, supportKind)))
            .Any(child => !providerChildren.Any(providerChild =>
                providerChild.TargetEntityId == child.Entity.Id ||
                IsSameStructuralChild(child, providerChild, cautious: false)));
    }

    /// <summary>
    /// Swaps an unbound container's child shells for their resolved versions as the cascade
    /// produces them. An unbound provider container (e.g. a MangaDex volume for a flat-scanned
    /// book) carries shells of the same children the cascade resolves at the parent level; the
    /// resolved node replaces its shell inside the container and stays out of the top level so
    /// each local entity appears exactly once, nested under the structure the provider proposes.
    /// </summary>
    private static (IReadOnlyList<EntityMetadataProposal> Containers, IReadOnlyList<EntityMetadataProposal> Remaining) AdoptResolvedChildrenIntoContainers(
        IReadOnlyList<EntityMetadataProposal> baseChildren,
        IReadOnlyList<EntityMetadataProposal> resolved) {
        if (baseChildren.Count == 0 || resolved.Count == 0) {
            return (baseChildren, resolved);
        }

        var adopted = new HashSet<Guid>();
        var containers = baseChildren
            .Select(container => {
                if (container.TargetEntityId is not null ||
                    EntityMetadataProposalTraversal.IsRelationshipKind(container.TargetKind) ||
                    container.Children.Count == 0) {
                    return container;
                }

                var swapped = container.Children
                    .Select(shell => {
                        var match = resolved.FirstOrDefault(candidate => IsSameAdoptableNode(shell, candidate));
                        if (match?.TargetEntityId is not { } targetId) {
                            return shell;
                        }

                        adopted.Add(targetId);
                        return match;
                    })
                    .ToArray();
                return container with { Children = swapped };
            })
            .ToArray();
        var remaining = resolved
            .Where(child => child.TargetEntityId is not { } targetId || !adopted.Contains(targetId))
            .ToArray();
        return (containers, remaining);
    }

    private static bool IsSameAdoptableNode(EntityMetadataProposal shell, EntityMetadataProposal resolved) =>
        (!string.IsNullOrWhiteSpace(shell.ProposalId) && shell.ProposalId == resolved.ProposalId) ||
        (shell.TargetEntityId is { } target && target == resolved.TargetEntityId) ||
        IsSameStructuralChild(shell, resolved);

    private static IReadOnlyList<EntityMetadataProposal> MergeStructuralChildren(
        IReadOnlyList<EntityMetadataProposal> providerChildren,
        IReadOnlyList<EntityMetadataProposal> localChildren) {
        // Single-source lists still normalize: duplicate nodes for the same target entity would
        // otherwise apply twice and write colliding titles and sort orders.
        if (providerChildren.Count == 0) {
            return NormalizeStructuralChildren(localChildren);
        }

        if (localChildren.Count == 0) {
            return NormalizeStructuralChildren(providerChildren);
        }

        var merged = new List<EntityMetadataProposal>(providerChildren);
        foreach (var localChild in localChildren) {
            var existingIndex = merged.FindIndex(providerChild => IsSameStructuralChild(providerChild, localChild));
            if (existingIndex >= 0) {
                merged[existingIndex] = localChild;
                continue;
            }

            merged.Add(localChild);
        }

        return NormalizeStructuralChildren(merged);
    }

    private static IReadOnlyList<EntityMetadataProposal> NormalizeStructuralChildren(
        IReadOnlyList<EntityMetadataProposal> children) =>
        children
            .Select((child, index) => new { Child = child, Index = index })
            .GroupBy(row => StructuralChildKey(row.Child), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => StructuralSortOrder(row.Child) is null)
            .ThenBy(row => StructuralSortOrder(row.Child))
            .ThenBy(row => row.Index)
            .Select(row => row.Child)
            .ToArray();

    private static string StructuralChildKey(EntityMetadataProposal child) {
        if (child.TargetEntityId is { } targetEntityId) {
            return $"id:{targetEntityId}";
        }

        var sortOrder = StructuralSortOrder(child);
        if (sortOrder is not null) {
            return $"position:{StructuralKindKey(child.TargetKind)}:{sortOrder}";
        }

        if (!string.IsNullOrWhiteSpace(child.ProposalId)) {
            return $"proposal:{child.ProposalId}";
        }

        return $"title:{StructuralKindKey(child.TargetKind)}:{child.Patch.Title?.Trim()}";
    }

    private static int? StructuralSortOrder(EntityMetadataProposal child) =>
        EntityMetadataPositionRules.SortOrderFor(
            child.TargetKind.ToEntityKind().ToCode(),
            EntityMetadataPositionRules.Normalize(child.Patch.Positions));

    // The structural key collapses a proposal kind to the entity kind it persists as, so a
    // provider's "video-episode" leaf and a local "video" sort/dedup into the same bucket.
    private static string StructuralKindKey(ProposalKind kind) =>
        kind.ToEntityKind().ToCode();

    private static bool IsSameStructuralChild(EntityMetadataProposal left, EntityMetadataProposal right) =>
        StructuralChildMatcher.IsSameProposalChild(left, right);

    /// <summary>
    /// Guards against a proposal whose title is just the provider's own identifier. Some providers
    /// fall back to the raw external id for the title when a detail lookup returns nothing (e.g. a
    /// transient upstream failure), which would otherwise surface — and apply — a bare GUID as the
    /// entity's name. When the proposed title is empty or equals one of the proposal's own external
    /// id values, fall back to the local entity's existing title so the UI and apply never write a
    /// raw id. This is provider-agnostic: any plugin that degrades this way is covered.
    /// </summary>
    private static EntityMetadataProposal EnsureMeaningfulTitle(EntityMetadataProposal proposal, string localTitle) {
        var patch = proposal.Patch;
        if (patch is null || string.IsNullOrWhiteSpace(localTitle)) {
            return proposal;
        }

        var title = patch.Title?.Trim();
        var titleIsRawId = string.IsNullOrEmpty(title) ||
            (patch.ExternalIds is not null &&
             patch.ExternalIds.Values.Any(value => string.Equals(value, title, StringComparison.OrdinalIgnoreCase)));
        return titleIsRawId
            ? proposal with { Patch = patch with { Title = localTitle } }
            : proposal;
    }

    private async Task<EntityMetadataProposal> BindLocalStructuralTargetsAsync(
        EntityMetadataProposal proposal,
        Guid parentEntityId,
        CancellationToken cancellationToken) {
        var proposalChildren = proposal.Children ?? [];
        if (proposalChildren.Count == 0) {
            return proposal;
        }

        var localChildren = await LoadStructuralChildrenAsync(parentEntityId, cancellationToken);
        var cautiousStructuralMatching = ShouldUseCautiousStructuralMatching(localChildren, proposalChildren);
        var usedLocalEntityIds = new HashSet<Guid>();
        var children = new List<EntityMetadataProposal>(proposalChildren.Count);
        foreach (var childProposal in proposalChildren) {
            if (EntityMetadataProposalTraversal.IsRelationshipKind(childProposal.TargetKind)) {
                children.Add(childProposal);
                continue;
            }

            var localChild = StructuralChildMatcher.FindLocalChild(
                childProposal,
                localChildren.Select(ToMatchInput).ToArray(),
                usedLocalEntityIds,
                cautiousStructuralMatching);
            if (localChild is null) {
                // No local entity matches this provider child. Preserve it unbound only when the
                // provider advertised it as a structural container (e.g. a season the library has
                // not scanned yet, or a volume for a book scanned without volume folders) so it can
                // be materialized as a new child. Leaf children the provider cascaded (episodes,
                // etc.) are dropped — we never invent playable items that have no local media file.
                if (IsProviderAdvertisedStructuralChild(childProposal) ||
                    IsStructuralContainerKind(childProposal.TargetKind)) {
                    // Deliberately kept with its child shells UNBOUND: a bound child in the
                    // streamed proposal means "resolved", so pre-binding the shells here would
                    // show the whole tree as matched while the cascade is still running. The
                    // root builder swaps each shell for its resolved, bound node as the cascade
                    // produces it (see AdoptResolvedChildrenIntoContainers).
                    children.Add(childProposal);
                }

                continue;
            }

            var boundChild = await BindLocalStructuralTargetsAsync(
                childProposal with { TargetEntityId = localChild.EntityId },
                localChild.EntityId,
                cancellationToken);
            children.Add(EnsureMeaningfulTitle(boundChild, localChild.Title));
        }

        return proposal with { Children = children };
    }

    /// <summary>
    /// Marks structural children the provider advertises as part of its own hierarchy (such as a
    /// season or volume container) rather than leaf media cascaded from a parent identify. These
    /// are preserved when no local entity matches so the user can materialize the missing
    /// structure; leaf children with no local media file are not.
    /// </summary>
    private const string ProviderAdvertisedStructuralMatchReason = "provider-tree";

    private static bool IsProviderAdvertisedStructuralChild(EntityMetadataProposal proposal) =>
        string.Equals(
            proposal.MatchReason,
            ProviderAdvertisedStructuralMatchReason,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a proposal kind persists as an identify-container entity kind (volume, season,
    /// album, …) — structure a provider may legitimately introduce around existing local media,
    /// as opposed to a playable leaf that must come from a scanned file.
    /// </summary>
    private static bool IsStructuralContainerKind(ProposalKind kind) =>
        EntityKindRegistry.EnumeratesIdentifyChildren(kind.ToEntityKind().ToCode());

    private static bool IsSameStructuralChild(StructuralChild localChild, EntityMetadataProposal proposal, bool cautious = false) =>
        StructuralChildMatcher.IsSameLocalAndProviderChild(ToMatchInput(localChild), proposal, cautious);

    private static bool IsCompatibleStructuralKind(string localKind, ProposalKind proposalKind) =>
        StructuralChildMatcher.IsCompatibleStructuralKind(localKind, proposalKind);

    private static bool ShouldUseCautiousStructuralMatching(
        IReadOnlyList<StructuralChild> localChildren,
        IReadOnlyList<EntityMetadataProposal> providerChildren) =>
        providerChildren.Count > 0 && localChildren.Count != providerChildren.Count;

    private static EntityMetadataProposal EnsureStructuralPositions(EntityMetadataProposal proposal, StructuralChild child) {
        if (child.SortOrder is not { } sortOrder || proposal.Patch.Positions.Count > 0) {
            return proposal;
        }

        var code = child.Entity.KindCode.Equals(EntityKindRegistry.VideoSeason.Code, StringComparison.OrdinalIgnoreCase)
            ? "seasonNumber"
            : "sortOrder";
        return proposal with {
            Patch = proposal.Patch with {
                Positions = new Dictionary<string, int> { [code] = sortOrder }
            }
        };
    }

    private static EntityMetadataProposal UnmatchedLocalStructuralChild(StructuralChild child, string provider) =>
        new(
            ProposalId: $"local-unmatched:{child.Entity.Id}",
            Provider: provider,
            TargetKind: child.Entity.KindCode.DecodeAs<EntityKind>().ToProposalKind(),
            Confidence: null,
            MatchReason: "local-unmatched",
            Patch: new EntityMetadataPatch(
                child.Entity.Title,
                Description: null,
                ExternalIds: new Dictionary<string, string>(),
                Urls: [],
                Tags: [],
                Studio: null,
                Credits: [],
                Dates: new Dictionary<string, string>(),
                Stats: new Dictionary<string, int>(),
                Positions: new Dictionary<string, int>(),
                Classification: null),
            Images: [],
            Children: [],
            Candidates: [],
            TargetEntityId: child.Entity.Id,
            Relationships: []);

    private static bool IsLocalUnmatchedProposal(EntityMetadataProposal proposal) =>
        proposal.ProposalId.StartsWith("local-unmatched:", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(proposal.MatchReason, "local-unmatched", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the caller is steering this identify by hand — a manual title search or an
    /// explicit pick-from-candidates request — so the entity's stored ids and urls must not
    /// route the plugin back onto the very match the user is trying to replace.
    /// </summary>
    private static bool ShouldIgnoreExistingIdentityHints(IdentifyQuery? query) =>
        (query?.RequireChoice == true || !string.IsNullOrWhiteSpace(query?.Title)) &&
        string.IsNullOrWhiteSpace(query?.Url) &&
        query?.ExternalIds is not { Count: > 0 };

    private async Task<IReadOnlyList<IdentifyEntitySnapshot>> LoadAncestorSnapshotsAsync(
        EntityRow entity,
        string providerId,
        CancellationToken cancellationToken) {
        var ancestors = new List<IdentifyEntitySnapshot>();
        var parentId = entity.ParentEntityId;
        var visited = new HashSet<Guid> { entity.Id };
        while (parentId is { } id && visited.Add(id)) {
            var parent = await _db.Entities
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (parent is null) {
                break;
            }

            ancestors.Add(await SnapshotAsync(parent, providerId, cancellationToken));
            parentId = parent.ParentEntityId;
        }

        return ancestors;
    }

    /// <summary>
    /// Merges client-supplied parent provider IDs (from a parent proposal not yet applied) into the
    /// immediate ancestor snapshot so a plugin can resolve a child within its parent's context.
    /// </summary>
    private static IReadOnlyList<IdentifyEntitySnapshot> MergeImmediateParentExternalIds(
        IReadOnlyList<IdentifyEntitySnapshot> ancestors,
        IReadOnlyDictionary<string, string>? parentExternalIds) {
        if (parentExternalIds is not { Count: > 0 } || ancestors.Count == 0) {
            return ancestors;
        }

        var immediate = ancestors[0];
        var merged = new Dictionary<string, string>(immediate.ExternalIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parentExternalIds) {
            merged[key] = value;
        }

        var updated = new List<IdentifyEntitySnapshot>(ancestors) { [0] = immediate with { ExternalIds = merged } };
        return updated;
    }

    private async Task<IdentifyEntitySnapshot> SnapshotAsync(
        EntityRow entity,
        string providerId,
        CancellationToken cancellationToken,
        EntityKind? kindOverride = null) {
        var hints = await _hints.ResolveAsync(entity.Id, providerId, cancellationToken);
        return new IdentifyEntitySnapshot(
            entity.Id,
            kindOverride ?? entity.KindCode.DecodeAs<EntityKind>(),
            entity.Title,
            hints.ExternalIds,
            hints.Urls);
    }

    private async Task<IdentifyEntitySnapshot> SnapshotFromProposalAsync(
        EntityRow entity,
        string providerId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        var current = await SnapshotAsync(entity, providerId, cancellationToken);
        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in current.ExternalIds ?? new Dictionary<string, string>()) {
            externalIds[key] = value;
        }

        foreach (var (key, value) in proposal.Patch.ExternalIds) {
            externalIds[key] = value;
        }

        var urls = new List<string>();
        urls.AddRange(current.Urls ?? []);
        urls.AddRange(proposal.Patch.Urls);

        var title = !string.IsNullOrWhiteSpace(proposal.Patch.Title)
            ? proposal.Patch.Title.Trim()
            : current.Title;

        return current with {
            Title = title,
            ExternalIds = externalIds,
            Urls = urls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    /// <summary>
    /// Loads every persisted structural child for provider binding and duplicate suppression. Wanted
    /// and fileless children remain matchable here so an equivalent provider container never becomes
    /// an unbound materialization candidate; callers that recurse must select
    /// <see cref="StructuralChild.IsIdentifyEligible"/>.
    /// </summary>
    private async Task<IReadOnlyList<StructuralChild>> LoadStructuralChildrenAsync(
        Guid parentEntityId,
        CancellationToken cancellationToken) {
        var children = await _db.Entities
            .AsNoTracking()
            .Where(row => row.ParentEntityId == parentEntityId)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);

        var eligibility = await _eligibility.EvaluateManyAsync(
            children.Select(row => row.Id).ToArray(),
            cancellationToken);

        return children
            .Select(row => new StructuralChild(row.SortOrder, row, eligibility[row.Id].IsEligible))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, int>> ResolveStructuralPositionsAsync(
        Guid entityId,
        int? parentSortOrder,
        CancellationToken cancellationToken) {
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (parentSortOrder is { } sortOrder) {
            positions["sortOrder"] = sortOrder;
        }

        var persisted = await _db.EntityPositions
            .AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        foreach (var row in persisted) {
            positions[row.Code] = row.Value;
        }

        var seasonNumber = await _db.Entities
            .AsNoTracking()
            .Where(row => row.Id == entityId && row.KindCode == EntityKindRegistry.VideoSeason.Code)
            .Select(row => row.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
        if (seasonNumber is { } value) {
            positions["seasonNumber"] = value;
        }

        return positions;
    }

    private static bool SupportsKind(PluginManifest manifest, string kind) =>
        manifest.Supports.Any(support => PluginEntityKindCompatibility.SupportsKind(support, kind));

    private sealed record StructuralChild(int? SortOrder, EntityRow Entity, bool IsIdentifyEligible);

    private sealed record RelocatableLocalChild(Guid EntityId, string KindCode, string Title, int? SortOrder);

    private static StructuralLocalChild ToMatchInput(StructuralChild child) =>
        new(child.Entity.Id, child.Entity.KindCode, child.Entity.Title, child.SortOrder);
}
