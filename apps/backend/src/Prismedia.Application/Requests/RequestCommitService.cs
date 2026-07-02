using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Resolves full plugin metadata proposals for provider-qualified request ids (no library entity involved).</summary>
public interface IPluginRequestProposalSource {
    /// <summary>
    /// Resolves the proposal for a provider work-id of the descriptor's kind; structural children are
    /// included on request. Null when the provider is missing/disabled/NSFW-gated or can't resolve it.
    /// </summary>
    Task<EntityMetadataProposal?> ResolveProposalAsync(RequestKindDescriptor descriptor, string providerId, string itemId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken);
}

/// <summary>Result of ensuring a wanted entity: the entity, whether this call created it, and whether it already owns a real file.</summary>
public sealed record WantedEntityResult(Guid EntityId, bool Created, bool HasFile);

/// <summary>
/// Creates and populates wanted library entities for request commits. A wanted entity is a real library
/// entity (grid-visible immediately) flagged Wanted, carrying plugin metadata and artwork but no file;
/// the acquisition import later attaches the file and clears the flag.
/// </summary>
public interface IWantedEntityWriter {
    /// <summary>
    /// Finds the library entity for (kind, provider external id) or creates a wanted skeleton for it
    /// (flagged Wanted, stamped with the provider external id, parented when a parent is given).
    /// Existing entities are matched external-id-first, mirroring the identify apply rule.
    /// <paramref name="matchTitleKindWide"/> additionally allows a kind-wide case-insensitive title
    /// match — right for container groupings (an already-scanned author or artist folder that has no
    /// provider ids yet), too weak a signal for leaves, which only title-match inside their parent.
    /// </summary>
    Task<WantedEntityResult> EnsureAsync(EntityKind kind, string providerId, string itemId, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken);

    /// <summary>Applies a plugin proposal to an entity through the shared metadata-apply cascade (all present fields, default artwork).</summary>
    Task ApplyProposalAsync(Guid entityId, EntityMetadataProposal proposal, CancellationToken cancellationToken);

    /// <summary>
    /// Hard-deletes a request-created wanted entity — the cancel path's other half: cancelling a request
    /// removes the placeholder it created. Deletes nothing when the entity is gone, no longer Wanted, or
    /// owns a real file (an import won the race). Removing the last child of a wanted, fileless container
    /// removes the container too. Returns true when the entity was deleted.
    /// </summary>
    Task<bool> DeleteIfWantedAsync(Guid entityId, CancellationToken cancellationToken);
}

/// <summary>
/// Application use case for committing a reviewed request: Identify's apply, run on entities that don't
/// exist on disk yet. Kind behavior comes from <see cref="RequestKindRegistry"/> — a container commit
/// (author, artist) materializes the container plus its picked works; a leaf commit (book, movie, album)
/// materializes the item itself, or its picked sibling works. Every created entity is populated through
/// the shared metadata-apply cascade, and each requested leaf gets one acquisition linked to its wanted
/// entity so the import attaches the file to exactly that entity.
/// </summary>
public sealed class RequestCommitService(
    IPluginRequestProposalSource proposals,
    IWantedEntityWriter wanted,
    IAcquisitionRequestService acquisitions) {
    /// <summary>
    /// Commits a reviewed request. Returns null when the kind isn't committable, the provider-qualified
    /// id is malformed, or the provider can't resolve it; otherwise reports a per-item outcome (an
    /// already-owned or already-requested pick is skipped, not an error, so partial commits stay transparent).
    /// </summary>
    public async Task<RequestCommitResponse?> CommitAsync(RequestCommitRequest request, bool hideNsfw, CancellationToken cancellationToken) {
        var descriptor = RequestKindRegistry.Find(request.Kind);
        if (descriptor is not { Committable: true }) {
            return null;
        }

        var (providerId, itemId) = RequestProposalReading.SplitProviderQualifiedId(request.ExternalId);
        if (providerId is null || itemId is null) {
            return null;
        }

        return descriptor.IsContainer
            ? await CommitContainerAsync(descriptor, request, providerId, itemId, hideNsfw, cancellationToken)
            : await CommitLeafAsync(descriptor, request, providerId, itemId, hideNsfw, cancellationToken);
    }

    /// <summary>
    /// Commits a container request (author, artist): the container becomes a wanted grouping entity and
    /// each picked work a wanted leaf beneath it, each with its own acquisition.
    /// </summary>
    private async Task<RequestCommitResponse?> CommitContainerAsync(
        RequestKindDescriptor descriptor, RequestCommitRequest request, string providerId, string itemId, bool hideNsfw, CancellationToken cancellationToken) {
        var child = RequestKindRegistry.ChildOf(descriptor);
        if (child is null) {
            return null;
        }

        var proposal = await proposals.ResolveProposalAsync(descriptor, providerId, itemId, hideNsfw, includeChildren: true, cancellationToken);
        if (proposal?.Patch is null) {
            return null;
        }

        var containerTitle = TitleOr(proposal.Patch.Title, itemId);
        var container = await wanted.EnsureAsync(
            descriptor.WantedEntityKind, providerId, itemId, containerTitle,
            parentEntityId: null, matchTitleKindWide: true, cancellationToken);

        var picks = new List<CommitPick>();
        foreach (var childProposal in SelectStructuralChildren(providerId, proposal, request.SelectedChildIds)) {
            var pick = await EnsurePickAsync(child, providerId, childProposal, container.EntityId, cancellationToken);
            if (pick is not null) {
                picks.Add(pick);
            }
        }

        // Apply the proposal filtered to the picked (fileless) works: the cascade finds the pre-created
        // skeletons external-id-first and enriches them in place, and never materializes unpicked works.
        // Owned works are excluded so a request can't overwrite metadata the library already has.
        var applyChildren = proposal.Children.Where(node => node.TargetKind.IsRelationship())
            .Concat(picks.Where(pick => !pick.Entity.HasFile).Select(pick => pick.Proposal))
            .ToArray();
        await wanted.ApplyProposalAsync(container.EntityId, proposal with { Children = applyChildren }, cancellationToken);

        var items = new List<RequestCommitItem>();
        foreach (var pick in picks) {
            items.Add(await StartAcquisitionAsync(pick, providerId, child.AcquisitionKind, author: containerTitle, series: null, cancellationToken));
        }

        return new RequestCommitResponse(container.EntityId, items);
    }

    /// <summary>
    /// Commits a leaf request (book, movie, album). With no picked children the item requests itself;
    /// with picked children (a book's series volumes) each pick becomes its own standalone wanted leaf.
    /// </summary>
    private async Task<RequestCommitResponse?> CommitLeafAsync(
        RequestKindDescriptor descriptor, RequestCommitRequest request, string providerId, string itemId, bool hideNsfw, CancellationToken cancellationToken) {
        var includeChildren = request.SelectedChildIds.Count > 0 && descriptor.ChildKind is not null;
        var proposal = await proposals.ResolveProposalAsync(descriptor, providerId, itemId, hideNsfw, includeChildren, cancellationToken);
        if (proposal?.Patch is null) {
            return null;
        }

        var creator = RequestProposalReading.AuthorFromCredits(proposal.Patch) ?? RequestProposalReading.PrimaryCredit(proposal.Patch);
        if (!includeChildren) {
            var pick = await EnsurePickAsync(descriptor, providerId, proposal, parentEntityId: null, cancellationToken);
            if (pick is null) {
                return null;
            }

            if (!pick.Entity.HasFile) {
                await wanted.ApplyProposalAsync(pick.Entity.EntityId, proposal, cancellationToken);
            }

            return new RequestCommitResponse(
                null,
                [await StartAcquisitionAsync(pick, providerId, descriptor.AcquisitionKind, creator, series: null, cancellationToken)]);
        }

        // Sibling-work fan-out (a book's series volumes): each pick is its own standalone leaf request.
        var child = RequestKindRegistry.ChildOf(descriptor)!;
        var items = new List<RequestCommitItem>();
        foreach (var childProposal in SelectStructuralChildren(providerId, proposal, request.SelectedChildIds)) {
            var pick = await EnsurePickAsync(child, providerId, childProposal, parentEntityId: null, cancellationToken);
            if (pick is null) {
                continue;
            }

            if (!pick.Entity.HasFile) {
                await wanted.ApplyProposalAsync(pick.Entity.EntityId, pick.Proposal, cancellationToken);
            }

            items.Add(await StartAcquisitionAsync(
                pick, providerId, child.AcquisitionKind, creator, series: TitleOr(proposal.Patch.Title, itemId), cancellationToken));
        }

        return new RequestCommitResponse(null, items);
    }

    /// <summary>One picked work resolved to its wanted entity and commit outcome.</summary>
    private sealed record CommitPick(EntityMetadataProposal Proposal, string WorkId, string Title, WantedEntityResult Entity, RequestCommitOutcome Outcome);

    /// <summary>Ensures the wanted entity for a picked work and decides its outcome, or null when the proposal carries no work id.</summary>
    private async Task<CommitPick?> EnsurePickAsync(
        RequestKindDescriptor descriptor, string providerId, EntityMetadataProposal proposal, Guid? parentEntityId, CancellationToken cancellationToken) {
        if (RequestProposalReading.WorkIdFor(providerId, proposal) is not { } workId) {
            return null;
        }

        var title = TitleOr(proposal.Patch?.Title, workId);
        var entity = await wanted.EnsureAsync(
            descriptor.WantedEntityKind, providerId, workId, title, parentEntityId,
            matchTitleKindWide: descriptor.IsContainer, cancellationToken);
        var outcome = entity.HasFile
            ? RequestCommitOutcome.AlreadyOwned
            : !entity.Created && await acquisitions.AnyForEntityAsync(entity.EntityId, cancellationToken)
                ? RequestCommitOutcome.AlreadyRequested
                : RequestCommitOutcome.Requested;
        return new CommitPick(proposal, workId, title, entity, outcome);
    }

    /// <summary>Starts the acquisition for a requested pick (no-op for owned/in-flight picks) and shapes its response item.</summary>
    private async Task<RequestCommitItem> StartAcquisitionAsync(
        CommitPick pick, string providerId, EntityKind acquisitionKind, string? author, string? series, CancellationToken cancellationToken) {
        Guid? acquisitionId = null;
        if (pick.Outcome == RequestCommitOutcome.Requested) {
            var summary = await acquisitions.CreateAndSearchAsync(
                new AcquisitionCreateRequest(
                    pick.Title,
                    author,
                    series,
                    RequestProposalReading.YearFromDates(pick.Proposal.Patch?.Dates ?? new Dictionary<string, string>()),
                    RequestProposalReading.BestImage(pick.Proposal),
                    providerId,
                    pick.WorkId,
                    pick.Proposal.Patch?.Description,
                    acquisitionKind,
                    pick.Entity.EntityId),
                cancellationToken);
            acquisitionId = summary.Id;
        }

        return new RequestCommitItem($"{providerId}:{pick.WorkId}", pick.Title, pick.Outcome, pick.Entity.EntityId, acquisitionId);
    }

    /// <summary>The structural (non-relationship) children whose provider-qualified ids were picked.</summary>
    private static IReadOnlyList<EntityMetadataProposal> SelectStructuralChildren(
        string providerId, EntityMetadataProposal proposal, IReadOnlyList<string> selectedChildIds) {
        var picked = new HashSet<string>(selectedChildIds, StringComparer.OrdinalIgnoreCase);
        return proposal.Children
            .Where(child => !child.TargetKind.IsRelationship())
            .Where(child => RequestProposalReading.QualifiedIdFor(providerId, child) is { } id && picked.Contains(id))
            .ToArray();
    }

    private static string TitleOr(string? title, string fallback) =>
        string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
}
