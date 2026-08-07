using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Starts an acquisition from an already-committed Entity graph. Background request fan-out depends on
/// this narrow seam so structural hydration can be ordered before acquisition/search publication.
/// </summary>
public interface IRequestGraphAcquisitionStarter {
    /// <summary>
    /// Persists the complete cached review graph before any acquisition derived from it can search.
    /// </summary>
    Task ApplyReviewedMetadataAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken);

    /// <summary>Starts or observes one graph-backed acquisition.</summary>
    Task<RequestCommitResponse?> RequestEntityFromGraphAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken,
        AcquisitionTargeting? targeting = null,
        BookRendition? bookRendition = null,
        bool hydrateChildren = true);

    /// <summary>Starts one acquisition while preserving an inherited graph or explicit root origin.</summary>
    Task<RequestCommitResponse?> RequestEntityFromGraphAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken,
        AcquisitionTargeting? targeting,
        BookRendition? bookRendition,
        bool hydrateChildren,
        JobContext? parentContext,
        JobGraphOrigin origin) =>
        RequestEntityFromGraphAsync(
            entityId,
            hideNsfw,
            cancellationToken,
            targeting,
            bookRendition,
            hydrateChildren);
}

public sealed partial class RequestCommitService {
    /// <inheritdoc />
    public Task ApplyReviewedMetadataAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) =>
        wanted.ApplyProposalWithDeferredArtworkAsync(entityId, proposal, cancellationToken);

    /// <summary>
    /// Requests an entity from its own graph with no provider round-trip. Deferred reviewed-container
    /// fan-out uses the already committed Entity metadata after structural children have been hydrated.
    /// </summary>
    public async Task<RequestCommitResponse?> RequestEntityFromGraphAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken,
        AcquisitionTargeting? targeting = null,
        BookRendition? bookRendition = null,
        bool hydrateChildren = true) {
        return await RequestEntityFromGraphAsync(
            entityId,
            hideNsfw,
            cancellationToken,
            targeting,
            bookRendition,
            hydrateChildren,
            parentContext: null,
            JobGraphOrigin.Interactive);
    }

    /// <inheritdoc />
    public async Task<RequestCommitResponse?> RequestEntityFromGraphAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken,
        AcquisitionTargeting? targeting,
        BookRendition? bookRendition,
        bool hydrateChildren,
        JobContext? parentContext,
        JobGraphOrigin origin) {
        var entity = await wanted.GetEntityAsync(entityId, cancellationToken);
        if (entity is null) {
            return null;
        }

        var descriptor = RequestKindRegistry.FindCommittableEntityRequest(entity.Kind, bookRendition);
        if (descriptor is null) {
            return null;
        }

        if (targeting is null || targeting.IsEmpty) {
            targeting = await InheritedTargetingAsync(entity, cancellationToken);
        }

        // A season pack cannot exist while any of its episodes are undated or still in the future.
        // Use the already-committed episode graph as the acquisition plan instead: aired episodes can
        // search now and each future episode enters its own release-date wait without touching indexers.
        if (descriptor.WantedEntityKind == EntityKind.VideoSeason && releaseTiming is not null) {
            var timing = await releaseTiming.EvaluateAsync(
                entity.EntityId,
                targeting.ProfileId,
                descriptor.AcquisitionKind,
                cancellationToken);
            if (timing.PreferChildAcquisitions) {
                var children = await RequestMissingChildItemsAsync(
                    entity.EntityId,
                    parentContext,
                    origin,
                    cancellationToken);
                return new RequestCommitResponse(null, children.Items);
            }
        }

        return await RequestFromEntityGraphAsync(
            descriptor,
            entity,
            targeting,
            hideNsfw,
            cancellationToken,
            hydrateChildren,
            parentContext,
            origin);
    }

    /// <summary>Starts one graph-backed request after resolving its descriptor and inherited targeting.</summary>
    private async Task<RequestCommitResponse?> RequestFromEntityGraphAsync(
        RequestKindDescriptor descriptor,
        MonitorableEntity entity,
        AcquisitionTargeting targeting,
        bool hideNsfw,
        CancellationToken cancellationToken,
        bool hydrateChildren = true,
        JobContext? parentContext = null,
        JobGraphOrigin origin = JobGraphOrigin.Interactive) {
        var primaryIdentity = entity.ProviderIdentity?.Identity
            ?? entity.ExternalIdentities.FirstOrDefault();
        var requestOwnedEntity = descriptor.AcquireFromEntity;
        if (entity.HasRendition(descriptor.BookRendition) && !requestOwnedEntity) {
            return new RequestCommitResponse(null, [Item(RequestCommitOutcome.AlreadyOwned, null)]);
        }

        if (await acquisitions.AnyOpenForEntityAsync(entity.EntityId, descriptor.BookRendition, cancellationToken)) {
            if (hydrateChildren && primaryIdentity is not null) {
                await EnsurePhantomDescendantsAsync(
                    descriptor,
                    primaryIdentity,
                    entity.EntityId,
                    entity.ProviderIdentity?.PluginId,
                    prepared: null,
                    hideNsfw,
                    cancellationToken);
            }
            return new RequestCommitResponse(null, [Item(RequestCommitOutcome.AlreadyRequested, null)]);
        }

        string? creator = null;
        string? series = null;
        var parentId = entity.ParentEntityId;
        var visitedAncestors = new HashSet<Guid>();
        while (parentId is { } id && visitedAncestors.Add(id)) {
            var ancestor = await wanted.GetEntityAsync(id, cancellationToken);
            if (ancestor is null) {
                break;
            }

            switch (EntityKindRegistry.Describe(ancestor.Kind).AcquisitionAncestorContextRole) {
                case AcquisitionAncestorContextRole.Creator:
                    creator ??= ancestor.Title;
                    break;
                case AcquisitionAncestorContextRole.Series:
                    series ??= ancestor.Title;
                    break;
                case AcquisitionAncestorContextRole.None:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Entity kind '{ancestor.Kind.ToCode()}' declares an unsupported acquisition ancestor context role.");
            }
            parentId = ancestor.ParentEntityId;
        }

        var intentIdentities = primaryIdentity is null
            ? entity.ExternalIdentities
            : [primaryIdentity];
        await suppressions.ClearAsync(intentIdentities, cancellationToken);

        var positions = entity.Positions ?? new Dictionary<string, int>();
        var summary = await acquisitions.CreateAndSearchAsync(
            new AcquisitionCreateRequest(
                entity.Title,
                creator,
                series,
                Year: null,
                PosterUrl: null,
                primaryIdentity?.Namespace,
                primaryIdentity?.Value,
                Description: null,
                descriptor.AcquisitionKind,
                entity.EntityId,
                targeting.ProfileId,
                targeting.TargetLibraryRootId,
                positions.TryGetValue(EntityPositionCodes.Season, out var season) ? season : null,
                positions.TryGetValue(EntityPositionCodes.Episode, out var episode) ? episode : null,
                positions.TryGetValue(EntityPositionCodes.Volume, out var volume) ? volume : null,
                descriptor.BookRendition),
            parentContext,
            origin,
            cancellationToken);
        await StartMonitorOrRollbackAcquisitionAsync(
            summary.Id,
            descriptor.AcquisitionKind,
            entity.Title,
            creator,
            cancellationToken);
        if (hydrateChildren && primaryIdentity is not null) {
            await EnsurePhantomDescendantsAsync(
                descriptor,
                primaryIdentity,
                entity.EntityId,
                entity.ProviderIdentity?.PluginId,
                prepared: null,
                hideNsfw,
                cancellationToken);
        }
        return new RequestCommitResponse(null, [Item(RequestCommitOutcome.Requested, summary.Id, summary.JobGraphId)]);

        RequestCommitItem Item(RequestCommitOutcome outcome, Guid? acquisitionId, Guid? jobGraphId = null) =>
            new(
                primaryIdentity is null
                    ? entity.EntityId.ToString()
                    : RequestProposalReading.FormatQualifiedIdentity(primaryIdentity),
                entity.Title, outcome, entity.EntityId, acquisitionId, jobGraphId);
    }

    /// <summary>
    /// Batches direct child materialization and acquisition duplicate checks for a reviewed container.
    /// </summary>
    private async Task<IReadOnlyList<CommitPick>> EnsurePicksAsync(
        RequestKindDescriptor descriptor,
        IReadOnlyList<ResolvedRequestProposalNode> nodes,
        Guid parentEntityId,
        CancellationToken cancellationToken,
        bool requestOwnedEntity = false) {
        if (nodes.Count == 0) {
            return [];
        }

        var titles = nodes.Select(node => TitleOr(node.Proposal.Patch?.Title, node.Identity.Value)).ToArray();
        var entities = await wanted.EnsureChildrenAsync(
            parentEntityId,
            nodes.Select((node, index) => new WantedEntityEnsureRequest(
                descriptor.WantedEntityKind,
                node.Identity,
                titles[index],
                descriptor.BookRendition,
                node.Proposal.TargetEntityId)).ToArray(),
            cancellationToken);
        if (entities.Count != nodes.Count) {
            throw new InvalidOperationException("Wanted child materialization did not preserve the reviewed selection.");
        }

        var duplicateCandidates = entities
            .Where(entity => !entity.Created && (!entity.HasRequestedRendition || requestOwnedEntity))
            .Select(entity => entity.EntityId)
            .ToArray();
        var openEntityIds = await acquisitions.FilterOpenEntityIdsAsync(
            duplicateCandidates,
            descriptor.BookRendition,
            cancellationToken);
        return nodes.Select((node, index) => {
            var entity = entities[index];
            var outcome = entity.HasRequestedRendition && !requestOwnedEntity
                ? RequestCommitOutcome.AlreadyOwned
                : !entity.Created && openEntityIds.Contains(entity.EntityId)
                    ? RequestCommitOutcome.AlreadyRequested
                    : RequestCommitOutcome.Requested;
            return new CommitPick(node.Proposal, node.Identity, titles[index], entity, outcome);
        }).ToArray();
    }
}
