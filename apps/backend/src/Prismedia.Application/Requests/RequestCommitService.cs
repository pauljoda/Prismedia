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

/// <summary>One provider identity an entity carries (from a request commit or from Identify).</summary>
public sealed record ProviderRef(string Provider, string ItemId);

/// <summary>
/// A library entity read for monitoring/removal: its kind, display title, the provider identities a
/// discovery sync can re-resolve it from, and whether it owns a real source file.
/// </summary>
public sealed record MonitorableContainer(Guid EntityId, EntityKind Kind, string Title, IReadOnlyList<ProviderRef> ProviderIds, bool HasSourceFile = false);

/// <summary>
/// The discovery blacklist: provider work identities the user removed from Wanted. Container sweeps
/// skip suppressed works so a removed phantom never reappears; explicitly requesting a work clears it.
/// </summary>
public interface IWantedSuppressionStore {
    /// <summary>Suppresses every given identity (idempotent per identity).</summary>
    Task SuppressAsync(IReadOnlyList<ProviderRef> identities, EntityKind kind, string title, CancellationToken cancellationToken);

    /// <summary>The subset of the given identities that are suppressed, as "provider:itemId" strings.</summary>
    Task<IReadOnlySet<string>> FilterSuppressedAsync(IReadOnlyList<ProviderRef> identities, CancellationToken cancellationToken);

    /// <summary>Clears the suppression for every given identity — a direct request un-blacklists the work.</summary>
    Task ClearAsync(IReadOnlyList<ProviderRef> identities, CancellationToken cancellationToken);
}

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

    /// <summary>
    /// Reads a library entity as a monitorable container: its kind, title, and provider identities.
    /// Null when the entity doesn't exist. Works for wanted placeholders and real scanned-in entities
    /// alike — an on-disk author identified from its files monitors exactly like a requested one.
    /// </summary>
    Task<MonitorableContainer?> GetContainerAsync(Guid entityId, CancellationToken cancellationToken);
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
    IAcquisitionRequestService acquisitions,
    Acquisition.IMonitorStore monitors,
    IWantedSuppressionStore suppressions) {
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
    /// each picked work a wanted leaf beneath it, each with its own auto-grabbing acquisition. The
    /// container itself is monitored so future works keep appearing.
    /// </summary>
    private async Task<RequestCommitResponse?> CommitContainerAsync(
        RequestKindDescriptor descriptor, RequestCommitRequest request, string providerId, string itemId, bool hideNsfw, CancellationToken cancellationToken) {
        var proposal = await proposals.ResolveProposalAsync(descriptor, providerId, itemId, hideNsfw, includeChildren: true, cancellationToken);
        if (proposal?.Patch is null) {
            return null;
        }

        return await CommitContainerCoreAsync(
            descriptor, providerId, itemId, proposal, request.SelectedChildIds,
            startAcquisitions: true, cancellationToken);
    }

    /// <summary>
    /// Requests an existing library entity by id — the phantom's "Search for release": resolves the
    /// entity's registry kind and tries each of its provider identities until one resolves (an entity
    /// carries non-plugin identifiers like ISBNs too, so the caller can't just pick the first), then
    /// runs the ordinary leaf commit — which dedupes onto this same entity and starts its auto-grabbing,
    /// monitored acquisition. Null when the entity is gone, isn't a committable leaf kind, or no
    /// provider can resolve it.
    /// </summary>
    public async Task<RequestCommitResponse?> RequestEntityAsync(Guid entityId, bool hideNsfw, CancellationToken cancellationToken) {
        var entity = await wanted.GetContainerAsync(entityId, cancellationToken);
        if (entity is null) {
            return null;
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { IsContainer: false, Committable: true } && candidate.WantedEntityKind == entity.Kind);
        if (descriptor is null) {
            return null;
        }

        foreach (var providerRef in entity.ProviderIds) {
            var request = new RequestCommitRequest(descriptor.Kind, $"{providerRef.Provider}:{providerRef.ItemId}", []);
            var response = await CommitLeafAsync(descriptor, request, providerRef.Provider, providerRef.ItemId, hideNsfw, cancellationToken);
            if (response is not null) {
                return response;
            }
        }

        return null;
    }

    /// <summary>
    /// Re-syncs a monitored container entity from its provider: resolves the container's proposal, and
    /// materializes any works the library doesn't have yet as clearly-badged wanted placeholders —
    /// phantoms with metadata and artwork but NO acquisition. Discovery never downloads on its own; the
    /// user requests a phantom (or its page's search action does) and only then does the auto-grabbing
    /// acquisition flow take over. Returns false when the entity is gone, isn't a monitorable container
    /// kind, or no provider can resolve it — the sweep pauses the monitor in that case.
    /// </summary>
    public async Task<bool> SyncContainerAsync(Guid entityId, CancellationToken cancellationToken) {
        var container = await wanted.GetContainerAsync(entityId, cancellationToken);
        if (container is null || container.ProviderIds.Count == 0) {
            return false;
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { IsContainer: true, Committable: true } && candidate.WantedEntityKind == container.Kind);
        if (descriptor is null) {
            return false;
        }

        foreach (var providerRef in container.ProviderIds) {
            // Conservative SFW default: the sweep has no user session (mirrors background enrichment).
            var proposal = await proposals.ResolveProposalAsync(
                descriptor, providerRef.Provider, providerRef.ItemId, hideNsfw: true, includeChildren: true, cancellationToken);
            if (proposal?.Patch is null) {
                continue;
            }

            var allChildIds = proposal.Children
                .Where(child => !child.TargetKind.IsRelationship())
                .Select(child => RequestProposalReading.QualifiedIdFor(providerRef.Provider, child))
                .Where(id => id is not null)
                .Select(id => id!)
                .ToArray();
            await CommitContainerCoreAsync(
                descriptor, providerRef.Provider, providerRef.ItemId, proposal, allChildIds,
                startAcquisitions: false, cancellationToken);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The shared container materialization: ensure the container and its picked works as wanted
    /// entities, apply the proposal filtered to the fileless picks, and keep the container monitored.
    /// <paramref name="startAcquisitions"/> separates an explicit request (each new pick gets an
    /// auto-grabbing, monitored acquisition, and any suppression on it is cleared) from monitor
    /// discovery (phantoms only — and works the user removed from Wanted stay suppressed).
    /// </summary>
    private async Task<RequestCommitResponse> CommitContainerCoreAsync(
        RequestKindDescriptor descriptor, string providerId, string itemId, EntityMetadataProposal proposal,
        IReadOnlyList<string> selectedChildIds, bool startAcquisitions, CancellationToken cancellationToken) {
        var child = RequestKindRegistry.ChildOf(descriptor)!;
        var containerTitle = TitleOr(proposal.Patch.Title, itemId);
        var container = await wanted.EnsureAsync(
            descriptor.WantedEntityKind, providerId, itemId, containerTitle,
            parentEntityId: null, matchTitleKindWide: true, cancellationToken);

        var selectedChildren = SelectStructuralChildren(providerId, proposal, selectedChildIds);
        if (!startAcquisitions) {
            // Discovery honors the blacklist: a work the user removed from Wanted never reappears as a
            // phantom. An explicit pick below (startAcquisitions) instead CLEARS its suppression.
            var candidates = selectedChildren
                .Select(node => new ProviderRef(providerId, RequestProposalReading.WorkIdFor(providerId, node)!))
                .ToArray();
            var suppressed = await suppressions.FilterSuppressedAsync(candidates, cancellationToken);
            selectedChildren = selectedChildren
                .Where(node => !suppressed.Contains($"{providerId}:{RequestProposalReading.WorkIdFor(providerId, node)}"))
                .ToArray();
        }

        var picks = new List<CommitPick>();
        foreach (var childProposal in selectedChildren) {
            var pick = await EnsurePickAsync(child, providerId, childProposal, container.EntityId, cancellationToken);
            if (pick is not null) {
                picks.Add(pick);
            }
        }

        // Apply the proposal filtered to the picked (fileless) works: the cascade finds the pre-created
        // skeletons external-id-first and enriches them in place, and never materializes unpicked works.
        // Owned works are excluded so a request can't overwrite metadata the library already has. A
        // discovery sync that found nothing new skips the apply entirely (no daily artwork churn).
        var anythingNew = picks.Any(pick => pick.Outcome == RequestCommitOutcome.Requested);
        if (anythingNew || startAcquisitions) {
            var applyChildren = proposal.Children.Where(node => node.TargetKind.IsRelationship())
                .Concat(picks.Where(pick => !pick.Entity.HasFile).Select(pick => pick.Proposal))
                .ToArray();
            await wanted.ApplyProposalAsync(container.EntityId, proposal with { Children = applyChildren }, cancellationToken);
        }

        // The container keeps watching for new works either way — requesting an author/artist implies
        // following them; the daily sweep then surfaces future works as phantoms.
        await monitors.StartForEntityAsync(container.EntityId, descriptor.WantedEntityKind, containerTitle, cancellationToken);

        var items = new List<RequestCommitItem>();
        foreach (var pick in picks) {
            items.Add(startAcquisitions
                ? await StartAcquisitionAsync(pick, providerId, child.AcquisitionKind, author: containerTitle, series: null, cancellationToken)
                : new RequestCommitItem($"{providerId}:{pick.WorkId}", pick.Title, pick.Outcome, pick.Entity.EntityId, null));
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

    /// <summary>
    /// Starts the acquisition for a requested pick (no-op for owned/in-flight picks) and shapes its
    /// response item. A wanted item is hands-off by default: the acquisition auto-grabs its best
    /// accepted release, and a monitor keeps re-searching on the schedule until it is acquired or the
    /// user turns monitoring off.
    /// </summary>
    private async Task<RequestCommitItem> StartAcquisitionAsync(
        CommitPick pick, string providerId, EntityKind acquisitionKind, string? author, string? series, CancellationToken cancellationToken) {
        // A direct request un-blacklists the work: if the user removed it from Wanted before, asking
        // for it again by name is the explicit signal to want it after all.
        await suppressions.ClearAsync(IdentitiesOf(pick, providerId), cancellationToken);

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
            await monitors.StartAsync(summary.Id, acquisitionKind, pick.Title, author, cancellationToken);
        }

        return new RequestCommitItem($"{providerId}:{pick.WorkId}", pick.Title, pick.Outcome, pick.Entity.EntityId, acquisitionId);
    }

    /// <summary>
    /// Removes wanted placeholders the user no longer wants: suppresses every provider identity each
    /// entity carries (so a container sweep never resurrects it as a phantom), tears down any
    /// acquisitions targeting it (removing in-flight downloads), and deletes the placeholder entity.
    /// Entities with a real source file are left untouched — on-disk items aren't "wanted" to remove.
    /// Returns how many entities were removed.
    /// </summary>
    public async Task<int> RemoveWantedAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) {
        var removed = 0;
        foreach (var entityId in entityIds.Distinct()) {
            var entity = await wanted.GetContainerAsync(entityId, cancellationToken);
            if (entity is null || entity.HasSourceFile) {
                continue;
            }

            await suppressions.SuppressAsync(entity.ProviderIds, entity.Kind, entity.Title, cancellationToken);

            // Deleting an acquisition removes its torrent/data and (being wanted-linked) the entity;
            // the direct delete below covers acquisition-less phantoms.
            foreach (var acquisitionId in await acquisitions.ListIdsForEntityAsync(entityId, cancellationToken)) {
                await acquisitions.DeleteAsync(acquisitionId, cancellationToken);
            }

            await wanted.DeleteIfWantedAsync(entityId, cancellationToken);
            removed++;
        }

        return removed;
    }

    /// <summary>Every provider identity a pick carries: the resolving provider's pair plus the proposal's external ids.</summary>
    private static IReadOnlyList<ProviderRef> IdentitiesOf(CommitPick pick, string providerId) {
        var identities = new List<ProviderRef> { new(providerId, pick.WorkId) };
        foreach (var (provider, value) in pick.Proposal.Patch?.ExternalIds ?? new Dictionary<string, string>()) {
            if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(value)) {
                identities.Add(new ProviderRef(provider, value));
            }
        }

        return identities;
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
