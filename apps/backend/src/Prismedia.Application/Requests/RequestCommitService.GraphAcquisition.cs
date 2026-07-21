using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Starts an acquisition from an already-committed Entity graph. Background request fan-out depends on
/// this narrow seam so structural hydration can be ordered before acquisition/search publication.
/// </summary>
public interface IRequestGraphAcquisitionStarter {
    /// <summary>Starts or observes one graph-backed acquisition.</summary>
    Task<RequestCommitResponse?> RequestEntityFromGraphAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken,
        AcquisitionTargeting? targeting = null,
        BookRendition? bookRendition = null,
        bool hydrateChildren = true);
}

public sealed partial class RequestCommitService {
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
        var entity = await wanted.GetEntityAsync(entityId, cancellationToken);
        if (entity is null) {
            return null;
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { IsContainer: false, Committable: true }
            && candidate.WantedEntityKind == entity.Kind
            && (entity.Kind != EntityKind.Book
                || candidate.BookRendition == (bookRendition ?? BookRendition.Ebook)));
        if (descriptor is null) {
            return null;
        }

        if (targeting is null || targeting.IsEmpty) {
            targeting = await InheritedTargetingAsync(entity, cancellationToken);
        }

        return await RequestFromEntityGraphAsync(
            descriptor,
            entity,
            targeting,
            hideNsfw,
            cancellationToken,
            hydrateChildren);
    }

    /// <summary>Starts one graph-backed request after resolving its descriptor and inherited targeting.</summary>
    private async Task<RequestCommitResponse?> RequestFromEntityGraphAsync(
        RequestKindDescriptor descriptor,
        MonitorableEntity entity,
        AcquisitionTargeting targeting,
        bool hideNsfw,
        CancellationToken cancellationToken,
        bool hydrateChildren = true) {
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

            creator ??= ancestor.Kind is EntityKind.BookAuthor or EntityKind.MusicArtist ? ancestor.Title : null;
            series ??= ancestor.Kind is EntityKind.VideoSeries or EntityKind.AudioLibrary ? ancestor.Title : null;
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
        return new RequestCommitResponse(null, [Item(RequestCommitOutcome.Requested, summary.Id)]);

        RequestCommitItem Item(RequestCommitOutcome outcome, Guid? acquisitionId) =>
            new(
                primaryIdentity is null
                    ? entity.EntityId.ToString()
                    : RequestProposalReading.FormatQualifiedIdentity(primaryIdentity),
                entity.Title, outcome, entity.EntityId, acquisitionId);
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
                descriptor.BookRendition)).ToArray(),
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
