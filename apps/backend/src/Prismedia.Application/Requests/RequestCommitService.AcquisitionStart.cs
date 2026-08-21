using Prismedia.Application.Acquisition;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

public sealed partial class RequestCommitService {
    /// <summary>One picked work resolved to its wanted entity and commit outcome.</summary>
    private sealed record CommitPick(
        EntityMetadataProposal Proposal,
        ExternalIdentity Identity,
        string Title,
        WantedEntityResult Entity,
        RequestCommitOutcome Outcome);

    /// <summary>A resolved container child paired with the request descriptor governing that exact kind.</summary>
    private sealed record DescribedCommitPick(RequestKindDescriptor Descriptor, CommitPick Pick);

    /// <summary>Ensures the wanted entity for one server-resolved proposal node and decides its outcome.</summary>
    private async Task<CommitPick?> EnsurePickAsync(
        RequestKindDescriptor descriptor,
        ResolvedRequestProposalNode node,
        Guid? parentEntityId,
        CancellationToken cancellationToken,
        bool requestOwnedEntity = false) {
        var title = TitleOr(node.Proposal.Patch?.Title, node.Identity.Value);
        var entity = await wanted.EnsureAsync(
            descriptor.WantedEntityKind, node.Identity, title, parentEntityId,
            matchTitleKindWide: descriptor.IsContainer, cancellationToken, descriptor.BookRendition);
        var outcome = entity.HasRequestedRendition && !requestOwnedEntity
            ? RequestCommitOutcome.AlreadyOwned
            : !entity.Created && await acquisitions.AnyOpenForEntityAsync(
                entity.EntityId, descriptor.BookRendition, cancellationToken)
                ? RequestCommitOutcome.AlreadyRequested
                : RequestCommitOutcome.Requested;
        return new CommitPick(node.Proposal, node.Identity, title, entity, outcome);
    }

    /// <summary>
    /// Starts the acquisition for a requested pick and shapes its response item. An in-flight pick is a
    /// no-op. A container child that is already owned can instead attach stable Entity monitor intent
    /// without creating acquisition work; this is how All/Future discovery remembers accepted on-disk
    /// children while child-off suppression remains authoritative.
    /// </summary>
    private async Task<RequestCommitItem> StartAcquisitionAsync(
        CommitPick pick,
        EntityKind acquisitionKind,
        BookRendition? bookRendition,
        string? author,
        string? series,
        AcquisitionTargeting targeting,
        CancellationToken cancellationToken,
        bool attachOwnedEntityMonitor = false,
        PluginIdentityRoute? ownedEntityProviderRoute = null) {
        Guid? acquisitionId = null;
        Guid? acquisitionGraphId = null;
        var lifecycleAccepted = await monitors.ExecuteIfEntityLifecycleMutableAsync(
            pick.Entity.EntityId,
            async leaseCancellationToken => {
                if (pick.Outcome == RequestCommitOutcome.AlreadyOwned && attachOwnedEntityMonitor) {
                    if (ownedEntityProviderRoute is null
                        || !await wanted.BindProviderIdentityAsync(
                            pick.Entity.EntityId,
                            ownedEntityProviderRoute,
                            leaseCancellationToken)) {
                        throw new RequestCommitValidationException(
                            $"'{pick.Title}' could not be monitored because its exact plugin identity route is unavailable.");
                    }

                    await suppressions.ClearAsync(IdentitiesOf(pick), leaseCancellationToken);
                    await monitors.StartForEntityAsync(
                        pick.Entity.EntityId,
                        acquisitionKind,
                        pick.Title,
                        targeting,
                        preset: null,
                        cancellationToken: leaseCancellationToken);
                    return;
                }

                await suppressions.ClearAsync(IdentitiesOf(pick), leaseCancellationToken);
                if (pick.Outcome != RequestCommitOutcome.Requested) {
                    return;
                }

                var patch = pick.Proposal.Patch;
                var summary = await acquisitions.CreateAndSearchWithinEntityLifecycleAsync(
                    new AcquisitionCreateRequest(
                        pick.Title,
                        author,
                        series,
                        patch is null ? null : RequestProposalReading.YearFromDates(patch),
                        RequestProposalReading.BestImage(pick.Proposal),
                        pick.Identity.Namespace,
                        pick.Identity.Value,
                        patch?.Description,
                        acquisitionKind,
                        pick.Entity.EntityId,
                        targeting.ProfileId,
                        targeting.TargetLibraryRootId,
                        patch is null ? null : RequestProposalReading.SeasonNumberOf(patch),
                        patch is null ? null : RequestProposalReading.EpisodeNumberOf(patch),
                        patch is null ? null : RequestProposalReading.VolumeNumberOf(patch),
                        bookRendition),
                    leaseCancellationToken);
                acquisitionId = summary.Id;
                acquisitionGraphId = summary.JobGraphId;
                await StartMonitorOrRollbackAcquisitionAsync(
                    summary.Id,
                    acquisitionKind,
                    pick.Title,
                    author,
                    leaseCancellationToken);
            },
            cancellationToken);
        if (!lifecycleAccepted) {
            throw LifecycleConflict();
        }

        return new RequestCommitItem(
            RequestProposalReading.FormatQualifiedIdentity(pick.Identity),
            pick.Title,
            pick.Outcome,
            pick.Entity.EntityId,
            acquisitionId,
            acquisitionGraphId);
    }

    private static AcquisitionConfigurationException LifecycleConflict() =>
        new(
            Prismedia.Contracts.System.ApiProblemCodes.AcquisitionInvalid,
            "This Entity is being cleaned up. Wait for that operation to finish, then request it again.");

    private async Task StartMonitorOrRollbackAcquisitionAsync(
        Guid acquisitionId,
        EntityKind kind,
        string title,
        string? author,
        CancellationToken cancellationToken) {
        try {
            await monitors.StartAsync(acquisitionId, kind, title, author, cancellationToken);
        } catch (AcquisitionConfigurationException) {
            await acquisitions.DeleteForUnmonitorAsync(acquisitionId, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Removes wanted placeholders through the shared durable Entity give-up boundary, preserving any
    /// Entity that owns or gains a source file while cleanup is in progress.
    /// </summary>
    public async Task<WantedRemovalResponse> RemoveWantedAsync(
        IReadOnlyList<Guid> entityIds,
        CancellationToken cancellationToken) {
        var removed = 0;
        var failures = new List<WantedRemovalFailure>();
        foreach (var entityId in entityIds.Distinct()) {
            var entity = await wanted.GetEntityAsync(entityId, cancellationToken);
            if (entity is null) {
                removed++;
                continue;
            }
            if (entity.HasSourceFile) {
                failures.Add(new WantedRemovalFailure(
                    entityId,
                    $"{entity.Title} now has files on disk and is no longer only a wanted placeholder."));
                continue;
            }

            var result = await entityGiveUp.GiveUpEntityAsync(entityId, cancellationToken);
            if (!result.Stopped) {
                failures.Add(new WantedRemovalFailure(
                    entityId,
                    result.Message ?? "The wanted Entity could not be removed safely. Retry after its acquisition cleanup is available."));
                continue;
            }

            if (await wanted.GetEntityAsync(entityId, cancellationToken) is null) {
                removed++;
                continue;
            }

            failures.Add(new WantedRemovalFailure(
                entityId,
                $"{entity.Title} gained files on disk while removal was in progress, so it was kept in the library."));
        }

        return new WantedRemovalResponse(removed, failures);
    }
}
