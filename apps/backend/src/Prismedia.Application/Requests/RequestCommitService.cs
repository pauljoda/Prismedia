using Prismedia.Application.Acquisition;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Resolves full plugin metadata proposals for persistent external identities (no library entity involved).</summary>
public interface IPluginRequestProposalSource {
    /// <summary>
    /// Resolves the proposal for an identity of the descriptor's kind; structural children are
    /// included on request. Null when no enabled plugin route can resolve it.
    /// </summary>
    Task<EntityMetadataProposal?> ResolveProposalAsync(
        RequestKindDescriptor descriptor,
        ExternalIdentity identity,
        bool hideNsfw,
        bool includeChildren,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves through the explicitly selected plugin and identity route. Implementations must
    /// validate the route and must not substitute another plugin that shares the namespace.
    /// </summary>
    Task<EntityMetadataProposal?> ResolveProposalAsync(
        RequestKindDescriptor descriptor,
        PluginIdentityRoute route,
        bool hideNsfw,
        bool includeChildren,
        CancellationToken cancellationToken);
}

/// <summary>Result of ensuring a wanted entity: the entity, whether this call created it, and whether it already owns a real file.</summary>
public sealed record WantedEntityResult(Guid EntityId, bool Created, bool HasFile);

/// <summary>
/// A library entity read for monitoring/removal: its kind, display title, the external identities a
/// discovery sync can re-resolve it from, and whether it owns a real source file.
/// </summary>
public sealed record MonitorableContainer(
    Guid EntityId, EntityKind Kind, string Title, IReadOnlyList<ExternalIdentity> ExternalIdentities,
    bool HasSourceFile = false, Guid? ParentEntityId = null,
    IReadOnlyDictionary<string, int>? Positions = null);

/// <summary>
/// The discovery blacklist: provider work identities the user removed from Wanted. Container sweeps
/// skip suppressed works so a removed phantom never reappears; explicitly requesting a work clears it.
/// </summary>
public interface IWantedSuppressionStore {
    /// <summary>Suppresses every given identity (idempotent per identity).</summary>
    Task SuppressAsync(IReadOnlyList<ExternalIdentity> identities, EntityKind kind, string title, CancellationToken cancellationToken);

    /// <summary>The subset of the given canonical identities that are suppressed.</summary>
    Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken);

    /// <summary>Clears the suppression for every given identity — a direct request un-blacklists the work.</summary>
    Task ClearAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken);
}

/// <summary>
/// Creates and populates wanted library entities for request commits. A wanted entity is a real library
/// entity (grid-visible immediately) flagged Wanted, carrying plugin metadata and artwork but no file;
/// the acquisition import later attaches the file and clears the flag.
/// </summary>
public interface IWantedEntityWriter {
    /// <summary>
    /// Finds the library entity for (kind, external identity) or creates a wanted skeleton for it
    /// (flagged Wanted, stamped with the identity, parented when a parent is given).
    /// Existing entities are matched external-id-first, mirroring the identify apply rule.
    /// <paramref name="matchTitleKindWide"/> additionally allows a kind-wide case-insensitive title
    /// match — right for container groupings (an already-scanned author or artist folder that has no
    /// provider ids yet), too weak a signal for leaves, which only title-match inside their parent.
    /// </summary>
    Task<WantedEntityResult> EnsureAsync(
        EntityKind kind,
        ExternalIdentity identity,
        string title,
        Guid? parentEntityId,
        bool matchTitleKindWide,
        CancellationToken cancellationToken);

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

    /// <summary>
    /// The still-wanted, fileless children of the given kind under an entity, in sort order — a
    /// season's missing episodes. These are the gaps a season-pack import left behind.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListWantedChildIdsAsync(Guid parentEntityId, EntityKind childKind, CancellationToken cancellationToken);

    /// <summary>Every child of the kind under a parent, wanted or owned — the recursion set for a deep missing-children sweep.</summary>
    Task<IReadOnlyList<Guid>> ListChildIdsAsync(Guid parentEntityId, EntityKind childKind, CancellationToken cancellationToken);
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
    /// Commits a reviewed request. Returns null when the kind isn't committable, the identity-qualified
    /// id is malformed, or no plugin can resolve it; otherwise reports a per-item outcome (an
    /// already-owned or already-requested pick is skipped, not an error, so partial commits stay transparent).
    /// </summary>
    public async Task<RequestCommitResponse?> CommitAsync(RequestCommitRequest request, bool hideNsfw, CancellationToken cancellationToken) {
        var descriptor = RequestKindRegistry.Find(request.Kind);
        if (descriptor is not { Committable: true }) {
            return null;
        }

        var identity = RequestProposalReading.ParseQualifiedIdentity(request.ExternalId);
        if (identity is null) {
            return null;
        }

        return descriptor.IsContainer
            ? await CommitContainerAsync(descriptor, request, identity, hideNsfw, cancellationToken)
            : await CommitLeafAsync(descriptor, request, identity, hideNsfw, cancellationToken);
    }

    /// <summary>
    /// Commits a container request (author, artist): the container becomes a wanted grouping entity and
    /// each picked work a wanted leaf beneath it, each with its own auto-grabbing acquisition. The
    /// container itself is monitored so future works keep appearing.
    /// </summary>
    private async Task<RequestCommitResponse?> CommitContainerAsync(
        RequestKindDescriptor descriptor, RequestCommitRequest request, ExternalIdentity identity, bool hideNsfw, CancellationToken cancellationToken) {
        var proposal = await proposals.ResolveProposalAsync(descriptor, identity, hideNsfw, includeChildren: true, cancellationToken);
        if (proposal?.Patch is null) {
            return null;
        }

        // An explicit child selection wins; a preset with no selection derives one. The default preset is
        // All, so an old client that sends neither behaves exactly as before (the endpoint requires at least
        // one selected child for a container, so this derive path is only reached when a preset is sent).
        var preset = request.Preset ?? MonitorPreset.All;
        var selectedChildIds = request.SelectedChildIds.Count > 0
            ? request.SelectedChildIds
            : MonitorPresetSelection.Resolve(preset, ContainerCandidates(identity.Namespace, proposal));

        return await CommitContainerCoreAsync(
            descriptor, identity.Namespace, identity.Value, proposal, selectedChildIds,
            requestOwnedChildren: request.SelectedChildIds.Count > 0,
            startAcquisitions: true, TargetingOf(request), preset, hideNsfw, cancellationToken);
    }

    /// <summary>
    /// The container's structural children as preset candidates: each carries its identity-qualified id,
    /// its season/volume number (when the provider declares one), and — at commit time — <c>Owned: false</c>
    /// (ownership dedup happens downstream in <see cref="EnsurePickAsync"/>/<see cref="StartAcquisitionAsync"/>,
    /// so All and Missing both pass every id here and differ only in the persisted preset's sync gate).
    /// </summary>
    private static IReadOnlyList<MonitorPresetCandidate> ContainerCandidates(string providerId, EntityMetadataProposal proposal) =>
        proposal.Children
            .Where(child => !child.TargetKind.IsRelationship())
            .Select(child => (Id: RequestProposalReading.QualifiedIdFor(providerId, child), child.Patch))
            .Where(candidate => candidate.Id is not null)
            .Select(candidate => new MonitorPresetCandidate(
                candidate.Id!,
                candidate.Patch is null ? null : RequestProposalReading.SeasonNumberOf(candidate.Patch),
                Owned: false))
            .ToArray();

    /// <summary>The request-time acquisition choices (import target, profile) carried by a commit.</summary>
    private static AcquisitionTargeting TargetingOf(RequestCommitRequest request) =>
        new(request.TargetLibraryRootId, request.ProfileId);

    /// <summary>
    /// Requests an existing library entity by id — the phantom's "Search for release": resolves the
    /// entity's registry kind and tries each of its external identities until one resolves (an entity
    /// carries non-plugin identifiers like ISBNs too, so the caller can't just pick the first), then
    /// runs the ordinary leaf commit — which dedupes onto this same entity and starts its auto-grabbing,
    /// monitored acquisition. Null when the entity is gone, isn't a committable leaf kind, or no
    /// provider can resolve it.
    /// </summary>
    public async Task<RequestCommitResponse?> RequestEntityAsync(
        Guid entityId, bool hideNsfw, CancellationToken cancellationToken, AcquisitionTargeting? targeting = null) {
        var entity = await wanted.GetContainerAsync(entityId, cancellationToken);
        if (entity is null) {
            return null;
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { IsContainer: false, Committable: true } && candidate.WantedEntityKind == entity.Kind);
        if (descriptor is null) {
            // A container phantom (a wanted series) has no acquirable unit of its own — requesting it
            // means requesting each of its still-wanted children (the series' unrequested seasons).
            return await RequestContainerChildrenAsync(entity, hideNsfw, cancellationToken);
        }

        // No explicit choices: inherit the nearest followed ancestor's (a phantom episode of a
        // monitored series should land where the series' request chose), else kind defaults apply.
        if (targeting is null || targeting.IsEmpty) {
            targeting = await InheritedTargetingAsync(entity, cancellationToken);
        }

        // TV units carry their search context on their ancestors (the series name, the season number),
        // and their providers cannot resolve them standalone — they acquire from the entity graph
        // directly. Every other kind re-resolves through its provider, degrading to the graph only
        // when no provider answers, so a work whose provider vanished can still be fetched.
        if (!descriptor.AcquireFromEntity) {
            foreach (var identity in entity.ExternalIdentities) {
                var request = new RequestCommitRequest(
                    descriptor.Kind, RequestProposalReading.FormatQualifiedIdentity(identity), [],
                    targeting.TargetLibraryRootId, targeting.ProfileId);
                var response = await CommitLeafAsync(
                    descriptor, request, identity, hideNsfw, cancellationToken);
                if (response is not null) {
                    return response;
                }
            }
        }

        return await RequestFromEntityGraphAsync(descriptor, entity, targeting, hideNsfw, cancellationToken);
    }

    /// <summary>
    /// The container half of <see cref="RequestEntityAsync"/>: a committable container kind (series,
    /// author, artist) is requested by requesting each of its still-wanted children, and the per-child
    /// outcomes roll up into one response. Null when the kind has no committable child kind or nothing
    /// under it is still wanted — the caller reports "not requestable" exactly as before.
    /// </summary>
    private async Task<RequestCommitResponse?> RequestContainerChildrenAsync(
        MonitorableContainer entity, bool hideNsfw, CancellationToken cancellationToken) {
        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { IsContainer: true, Committable: true } && candidate.WantedEntityKind == entity.Kind);
        var child = descriptor is null ? null : RequestKindRegistry.ChildOf(descriptor);
        if (child is not { Committable: true }) {
            return null;
        }

        var missing = await wanted.ListWantedChildIdsAsync(entity.EntityId, child.WantedEntityKind, cancellationToken);
        var items = new List<RequestCommitItem>();
        foreach (var childId in missing) {
            var response = await RequestEntityAsync(childId, hideNsfw, cancellationToken);
            if (response is not null) {
                items.AddRange(response.Items);
            }
        }

        return items.Count == 0 ? null : new RequestCommitResponse(entity.EntityId, items);
    }

    /// <summary>
    /// Requests every still-wanted descendant phantom under an entity — the season-pack completeness
    /// fallback. A season pack that imported with episodes missing chases each gap as its own
    /// monitored, auto-grabbing acquisition through the ordinary request pipeline (Sonarr's
    /// per-episode search); the manual "search missing" action rides the same path. A child already
    /// carrying an acquisition counts as covered rather than starting a duplicate. Owned container
    /// children are recursed into (an on-disk season under a series can still hold episode gaps), so a
    /// series-level sweep reaches every wanted descendant, not just wholly-missing seasons. Returns how
    /// many gaps are now covered and how many exist; (0, 0) when the entity is gone or its kind has no
    /// committable child kind.
    /// </summary>
    public async Task<(int Covered, int Missing)> RequestMissingChildrenAsync(Guid entityId, CancellationToken cancellationToken) {
        var entity = await wanted.GetContainerAsync(entityId, cancellationToken);
        if (entity is null) {
            return (0, 0);
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { Committable: true } && candidate.WantedEntityKind == entity.Kind);
        var child = descriptor is null ? null : RequestKindRegistry.ChildOf(descriptor);
        if (child is not { Committable: true }) {
            return (0, 0);
        }

        // Conservative SFW default, mirroring the discovery sweep: the fallback runs with no user
        // session, and the phantoms it requests were materialized under the same rule.
        var missing = await wanted.ListWantedChildIdsAsync(entityId, child.WantedEntityKind, cancellationToken);
        var covered = 0;
        foreach (var childId in missing) {
            var response = await RequestEntityAsync(childId, hideNsfw: true, cancellationToken);
            if (response is { Items.Count: > 0 }) {
                covered++;
            }
        }

        var total = missing.Count;

        // Recurse into the OWNED container children when the child kind has its own committable child
        // (a series' on-disk seasons). Wholly-wanted children were already requested above; recursing
        // into them too would double-request their gaps through the phantom-descendants path.
        if (RequestKindRegistry.ChildOf(child) is { Committable: true }) {
            var wantedSet = missing.ToHashSet();
            foreach (var ownedChildId in await wanted.ListChildIdsAsync(entityId, child.WantedEntityKind, cancellationToken)) {
                if (wantedSet.Contains(ownedChildId)) {
                    continue;
                }

                var (subCovered, subMissing) = await RequestMissingChildrenAsync(ownedChildId, cancellationToken);
                covered += subCovered;
                total += subMissing;
            }
        }

        return (covered, total);
    }

    /// <summary>The stored library/profile choices of the entity's nearest monitored ancestor, or none.</summary>
    private async Task<AcquisitionTargeting> InheritedTargetingAsync(MonitorableContainer entity, CancellationToken cancellationToken) {
        var parentId = entity.ParentEntityId;
        for (var depth = 0; parentId is { } id && depth < 3; depth++) {
            if (await monitors.GetTargetingByEntityAsync(id, cancellationToken) is { } stored) {
                return stored;
            }

            parentId = (await wanted.GetContainerAsync(id, cancellationToken))?.ParentEntityId;
        }

        return AcquisitionTargeting.None;
    }

    /// <summary>
    /// Requests an entity from its own graph — no provider round-trip. The wanted entity is already
    /// identified (that is the whole wanted design), so its title, positions (season/episode), and
    /// ancestor titles (series, author, artist) are enough to search releases. Used for kinds whose
    /// units cannot be provider-resolved standalone, and as the degrade path when providers fail.
    /// </summary>
    private async Task<RequestCommitResponse?> RequestFromEntityGraphAsync(
        RequestKindDescriptor descriptor, MonitorableContainer entity, AcquisitionTargeting targeting, bool hideNsfw, CancellationToken cancellationToken) {
        var primaryIdentity = entity.ExternalIdentities.FirstOrDefault();
        // Ordinary owned leaves are already satisfied. TV units are different: an existing season/episode
        // can still be explicitly monitored so the acquisition loop searches for missing files or cutoff
        // upgrades. That is the Season Pass toggle path for already-scanned series.
        var requestOwnedEntity = descriptor.AcquireFromEntity;
        if (entity.HasSourceFile && !requestOwnedEntity) {
            return new RequestCommitResponse(null, [Item(RequestCommitOutcome.AlreadyOwned, null)]);
        }

        if (await acquisitions.AnyForEntityAsync(entity.EntityId, cancellationToken)) {
            if (primaryIdentity is not null) {
                await EnsurePhantomDescendantsAsync(
                    descriptor,
                    primaryIdentity.Namespace,
                    primaryIdentity.Value,
                    entity.EntityId,
                    hideNsfw,
                    cancellationToken);
            }
            return new RequestCommitResponse(null, [Item(RequestCommitOutcome.AlreadyRequested, null)]);
        }

        // Ancestor context: the nearest creator grouping names the author/artist, the nearest series
        // names the TV context — the same drill-down rule the query ladder is built on.
        string? creator = null;
        string? series = null;
        var parentId = entity.ParentEntityId;
        for (var depth = 0; parentId is { } id && depth < 3; depth++) {
            var ancestor = await wanted.GetContainerAsync(id, cancellationToken);
            if (ancestor is null) {
                break;
            }

            creator ??= ancestor.Kind is EntityKind.BookAuthor or EntityKind.MusicArtist ? ancestor.Title : null;
            series ??= ancestor.Kind == EntityKind.VideoSeries ? ancestor.Title : null;
            parentId = ancestor.ParentEntityId;
        }

        // A direct request un-blacklists the work, exactly like the provider path.
        await suppressions.ClearAsync(entity.ExternalIdentities, cancellationToken);

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
                positions.TryGetValue(EntityPositionCodes.Volume, out var volume) ? volume : null),
            cancellationToken);
        await monitors.StartAsync(summary.Id, descriptor.AcquisitionKind, entity.Title, creator, cancellationToken);
        if (primaryIdentity is not null) {
            await EnsurePhantomDescendantsAsync(
                descriptor,
                primaryIdentity.Namespace,
                primaryIdentity.Value,
                entity.EntityId,
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
    /// Re-syncs a monitored container entity from its provider: resolves the container's proposal, and
    /// materializes any works the library doesn't have yet as clearly-badged wanted placeholders —
    /// phantoms with metadata and artwork but NO acquisition. Discovery never downloads on its own; the
    /// user requests a phantom (or its page's search action does) and only then does the auto-grabbing
    /// acquisition flow take over. Returns false when the entity is gone, isn't a monitorable container
    /// kind, or no provider can resolve it — the sweep pauses the monitor in that case.
    /// </summary>
    public async Task<bool> SyncContainerAsync(Guid entityId, CancellationToken cancellationToken) {
        var container = await wanted.GetContainerAsync(entityId, cancellationToken);
        if (container is null || container.ExternalIdentities.Count == 0) {
            return false;
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { IsContainer: true, Committable: true } && candidate.WantedEntityKind == container.Kind);
        if (descriptor is null) {
            return false;
        }

        // The container monitor's preset gates auto-discovery: only All and Future keep materializing
        // newly-discovered works as monitored wanted phantoms. Every other preset (Missing/FirstSeason/
        // LatestSeason/Pilot/None) keeps monitoring the works committed up front but ignores new arrivals,
        // so the sync re-resolves nothing new for them. A monitor with no stored preset is treated as All
        // (the default), preserving the pre-preset "always mirror the container" behavior.
        var preset = await monitors.GetPresetByEntityAsync(entityId, cancellationToken) ?? MonitorPreset.All;
        var autoMonitorsNewWorks = preset is MonitorPreset.All or MonitorPreset.Future;

        foreach (var identity in container.ExternalIdentities) {
            // Conservative SFW default: the sweep has no user session (mirrors background enrichment).
            var proposal = await proposals.ResolveProposalAsync(
                descriptor, identity, hideNsfw: true, includeChildren: true, cancellationToken);
            if (proposal?.Patch is null) {
                continue;
            }

            // Presets that do not auto-monitor new works pass no children, so a discovered work is never
            // materialized as a phantom; the container is still touched (kept alive) but nothing new appears.
            var childIds = autoMonitorsNewWorks
                ? proposal.Children
                    .Where(child => !child.TargetKind.IsRelationship())
                    .Select(child => RequestProposalReading.QualifiedIdFor(identity.Namespace, child))
                    .Where(id => id is not null)
                    .Select(id => id!)
                    .ToArray()
                : [];
            await CommitContainerCoreAsync(
                descriptor, identity.Namespace, identity.Value, proposal, childIds,
                requestOwnedChildren: false,
                startAcquisitions: false, AcquisitionTargeting.None, preset: null, hideNsfw: true, cancellationToken);
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
        IReadOnlyList<string> selectedChildIds, bool requestOwnedChildren, bool startAcquisitions, AcquisitionTargeting targeting,
        MonitorPreset? preset, bool hideNsfw, CancellationToken cancellationToken) {
        var child = RequestKindRegistry.ChildOf(descriptor)!;
        var containerTitle = TitleOr(proposal.Patch.Title, itemId);
        // The container's title is the child acquisitions' search context — creator context for an
        // author's books or an artist's albums, series context for a series' season packs.
        var (creatorContext, seriesContext) = descriptor.WantedEntityKind == EntityKind.VideoSeries
            ? ((string?)null, (string?)containerTitle)
            : (containerTitle, null);
        var container = await wanted.EnsureAsync(
            descriptor.WantedEntityKind, new ExternalIdentity(providerId, itemId), containerTitle,
            parentEntityId: null, matchTitleKindWide: true, cancellationToken);

        var selectedChildren = SelectStructuralChildren(providerId, proposal, selectedChildIds);
        if (!startAcquisitions) {
            // Discovery honors the blacklist: a work the user removed from Wanted never reappears as a
            // phantom. An explicit pick below (startAcquisitions) instead CLEARS its suppression.
            var candidates = selectedChildren
                .Select(node => TryIdentity(providerId, RequestProposalReading.IdentityValueFor(providerId, node)))
                .Where(identity => identity is not null)
                .Select(identity => identity!)
                .ToArray();
            var suppressed = await suppressions.FilterSuppressedAsync(candidates, cancellationToken);
            selectedChildren = selectedChildren
                .Where(node => TryIdentity(providerId, RequestProposalReading.IdentityValueFor(providerId, node)) is { } identity
                    && !suppressed.Contains(identity))
                .ToArray();
        }

        var picks = new List<CommitPick>();
        foreach (var childProposal in selectedChildren) {
            var pick = await EnsurePickAsync(
                child, providerId, childProposal, container.EntityId, cancellationToken,
                requestOwnedEntity: startAcquisitions && requestOwnedChildren && child.AcquireFromEntity);
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
        // following them; the daily sweep then surfaces future works as phantoms. The request's
        // library/profile choices and monitoring preset stick to the monitor so later phantom requests
        // inherit the choices and future syncs honor the preset (a sync, carrying neither, never clobbers
        // what an explicit request stored).
        await monitors.StartForEntityAsync(
            container.EntityId, descriptor.WantedEntityKind, containerTitle,
            startAcquisitions ? targeting : null, startAcquisitions ? preset : null, cancellationToken);

        var items = new List<RequestCommitItem>();
        foreach (var pick in picks) {
            items.Add(startAcquisitions
                ? await StartAcquisitionAsync(pick, providerId, child.AcquisitionKind, creatorContext, seriesContext, targeting, cancellationToken)
                : new RequestCommitItem(
                    RequestProposalReading.FormatQualifiedIdentity(new ExternalIdentity(providerId, pick.WorkId)),
                    pick.Title,
                    pick.Outcome,
                    pick.Entity.EntityId,
                    null));

            // A pick that nests further (a season's episodes) materializes its own children as wanted
            // phantoms — discovery only, never acquisitions; each phantom is requested from its page.
            await EnsurePhantomDescendantsAsync(child, providerId, pick, hideNsfw, cancellationToken);
        }

        return new RequestCommitResponse(container.EntityId, items);
    }

    /// <summary>
    /// Materializes a pick's own structural children as wanted phantoms when its kind nests further —
    /// the season→episode level of the TV chain, driven purely by the registry's child chain so a
    /// deeper medium is a registry row, not a new flow. Phantoms honor the discovery blacklist and
    /// never start acquisitions; owned children are skipped individually, so a partially-owned season
    /// still gets wanted placeholders for the missing episodes.
    /// </summary>
    private Task EnsurePhantomDescendantsAsync(
        RequestKindDescriptor pickDescriptor, string providerId, CommitPick pick, bool hideNsfw, CancellationToken cancellationToken) =>
        EnsurePhantomDescendantsAsync(
            pickDescriptor, providerId, pick.WorkId, pick.Entity.EntityId, hideNsfw, cancellationToken);

    private async Task EnsurePhantomDescendantsAsync(
        RequestKindDescriptor pickDescriptor, string providerId, string itemId, Guid parentEntityId, bool hideNsfw, CancellationToken cancellationToken) {
        var grandchild = RequestKindRegistry.ChildOf(pickDescriptor);
        if (grandchild is null) {
            return;
        }

        // The pick's own detail carries its children (a series proposal ships season shells only; the
        // season lookup ships the episodes), so resolve it with children included.
        var proposal = await proposals.ResolveProposalAsync(
            pickDescriptor,
            new ExternalIdentity(providerId, itemId),
            hideNsfw,
            includeChildren: true,
            cancellationToken);
        if (proposal?.Patch is null) {
            return;
        }

        var childProposals = proposal.Children.Where(node => !node.TargetKind.IsRelationship()).ToArray();
        if (childProposals.Length == 0) {
            return;
        }

        // Discovery honors the blacklist: a phantom the user removed from Wanted never reappears.
        var candidates = childProposals
            .Select(node => RequestProposalReading.IdentityValueFor(providerId, node))
            .Select(id => TryIdentity(providerId, id))
            .Where(identity => identity is not null)
            .Select(identity => identity!)
            .ToArray();
        var suppressed = await suppressions.FilterSuppressedAsync(candidates, cancellationToken);

        var phantomPicks = new List<CommitPick>();
        foreach (var childProposal in childProposals) {
            var workId = RequestProposalReading.IdentityValueFor(providerId, childProposal);
            if (TryIdentity(providerId, workId) is not { } identity || suppressed.Contains(identity)) {
                continue;
            }

            var phantom = await EnsurePickAsync(grandchild, providerId, childProposal, parentEntityId, cancellationToken);
            if (phantom is not null) {
                phantomPicks.Add(phantom);
            }
        }

        // Enrich the pick and its fresh phantoms through the shared cascade (titles, positions,
        // artwork); owned children are excluded so a request can't overwrite real metadata.
        if (phantomPicks.Any(phantom => phantom.Outcome == RequestCommitOutcome.Requested)) {
            var applyChildren = proposal.Children.Where(node => node.TargetKind.IsRelationship())
                .Concat(phantomPicks.Where(phantom => !phantom.Entity.HasFile).Select(phantom => phantom.Proposal))
                .ToArray();
            await wanted.ApplyProposalAsync(parentEntityId, proposal with { Children = applyChildren }, cancellationToken);
        }
    }

    /// <summary>
    /// Commits a leaf request (book, movie, album). With no picked children the item requests itself;
    /// with picked children (a book's series volumes) each pick becomes its own standalone wanted leaf.
    /// </summary>
    private async Task<RequestCommitResponse?> CommitLeafAsync(
        RequestKindDescriptor descriptor, RequestCommitRequest request, ExternalIdentity identity, bool hideNsfw, CancellationToken cancellationToken) {
        var includeChildren = request.SelectedChildIds.Count > 0 && descriptor.ChildKind is not null;
        var proposal = await proposals.ResolveProposalAsync(descriptor, identity, hideNsfw, includeChildren, cancellationToken);
        if (proposal?.Patch is null) {
            return null;
        }

        var providerId = identity.Namespace;
        var itemId = identity.Value;

        var creator = RequestProposalReading.AuthorFromCredits(proposal.Patch) ?? RequestProposalReading.PrimaryCredit(proposal.Patch);
        var targeting = TargetingOf(request);
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
                [await StartAcquisitionAsync(pick, providerId, descriptor.AcquisitionKind, creator, series: null, targeting, cancellationToken)]);
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
                pick, providerId, child.AcquisitionKind, creator, series: TitleOr(proposal.Patch.Title, itemId), targeting, cancellationToken));
        }

        return new RequestCommitResponse(null, items);
    }

    /// <summary>One picked work resolved to its wanted entity and commit outcome.</summary>
    private sealed record CommitPick(EntityMetadataProposal Proposal, string WorkId, string Title, WantedEntityResult Entity, RequestCommitOutcome Outcome);

    /// <summary>Ensures the wanted entity for a picked work and decides its outcome, or null when the proposal carries no work id.</summary>
    private async Task<CommitPick?> EnsurePickAsync(
        RequestKindDescriptor descriptor, string providerId, EntityMetadataProposal proposal, Guid? parentEntityId,
        CancellationToken cancellationToken, bool requestOwnedEntity = false) {
        if (RequestProposalReading.IdentityValueFor(providerId, proposal) is not { } workId) {
            return null;
        }

        var title = TitleOr(proposal.Patch?.Title, workId);
        var entity = await wanted.EnsureAsync(
            descriptor.WantedEntityKind, new ExternalIdentity(providerId, workId), title, parentEntityId,
            matchTitleKindWide: descriptor.IsContainer, cancellationToken);
        var outcome = entity.HasFile && !requestOwnedEntity
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
        CommitPick pick, string providerId, EntityKind acquisitionKind, string? author, string? series,
        AcquisitionTargeting targeting, CancellationToken cancellationToken) {
        // A direct request un-blacklists the work: if the user removed it from Wanted before, asking
        // for it again by name is the explicit signal to want it after all.
        await suppressions.ClearAsync(IdentitiesOf(pick, providerId), cancellationToken);

        Guid? acquisitionId = null;
        if (pick.Outcome == RequestCommitOutcome.Requested) {
            var patch = pick.Proposal.Patch;
            var summary = await acquisitions.CreateAndSearchAsync(
                new AcquisitionCreateRequest(
                    pick.Title,
                    author,
                    series,
                    RequestProposalReading.YearFromDates(patch?.Dates ?? new Dictionary<string, string>()),
                    RequestProposalReading.BestImage(pick.Proposal),
                    providerId,
                    pick.WorkId,
                    patch?.Description,
                    acquisitionKind,
                    pick.Entity.EntityId,
                    targeting.ProfileId,
                    targeting.TargetLibraryRootId,
                    patch is null ? null : RequestProposalReading.SeasonNumberOf(patch),
                    patch is null ? null : RequestProposalReading.EpisodeNumberOf(patch),
                    patch is null ? null : RequestProposalReading.VolumeNumberOf(patch)),
                cancellationToken);
            acquisitionId = summary.Id;
            await monitors.StartAsync(summary.Id, acquisitionKind, pick.Title, author, cancellationToken);
        }

        return new RequestCommitItem(
            RequestProposalReading.FormatQualifiedIdentity(new ExternalIdentity(providerId, pick.WorkId)),
            pick.Title,
            pick.Outcome,
            pick.Entity.EntityId,
            acquisitionId);
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

            await suppressions.SuppressAsync(entity.ExternalIdentities, entity.Kind, entity.Title, cancellationToken);

            // Deleting an acquisition removes its torrent/data but deliberately keeps the wanted entity
            // (a Downloads-view remove is not a give-up); this bulk remove IS the give-up, so the direct
            // delete below tears the placeholder down for both acquisition-linked and phantom entities.
            foreach (var acquisitionId in await acquisitions.ListIdsForEntityAsync(entityId, cancellationToken)) {
                await acquisitions.DeleteAsync(acquisitionId, cancellationToken);
            }

            await wanted.DeleteIfWantedAsync(entityId, cancellationToken);
            removed++;
        }

        return removed;
    }

    /// <summary>Every persistent identity a pick carries: the resolving provider's identity plus the proposal's external ids.</summary>
    private static IReadOnlyList<ExternalIdentity> IdentitiesOf(CommitPick pick, string providerId) {
        var identities = new Dictionary<string, ExternalIdentity>(StringComparer.Ordinal);
        AddIdentity(identities, providerId, pick.WorkId);
        foreach (var (provider, value) in pick.Proposal.Patch?.ExternalIds ?? new Dictionary<string, string>()) {
            AddIdentity(identities, provider, value);
        }

        return identities.Values.ToArray();
    }

    private static void AddIdentity(
        IDictionary<string, ExternalIdentity> identities,
        string? identityNamespace,
        string? value) {
        if (TryIdentity(identityNamespace, value) is { } identity) {
            identities.TryAdd(identity.Namespace, identity);
        }
    }

    private static ExternalIdentity? TryIdentity(string? identityNamespace, string? value) {
        if (string.IsNullOrWhiteSpace(identityNamespace) || string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        try {
            return new ExternalIdentity(identityNamespace, value);
        } catch (ArgumentException) {
            // Plugin proposals can carry transient lookup URLs in their external-id bag. They are
            // useful while resolving the proposal but are not stable persisted identities.
            return null;
        }
    }

    /// <summary>The structural children whose identity-qualified ids were picked.</summary>
    private static IReadOnlyList<EntityMetadataProposal> SelectStructuralChildren(
        string identityNamespace, EntityMetadataProposal proposal, IReadOnlyList<string> selectedChildIds) {
        var picked = selectedChildIds
            .Select(RequestProposalReading.ParseQualifiedIdentity)
            .Where(identity => identity is not null)
            .Select(identity => identity!)
            .ToHashSet();
        return proposal.Children
            .Where(child => !child.TargetKind.IsRelationship())
            .Where(child => RequestProposalReading.IdentityValueFor(identityNamespace, child) is { } value
                && picked.Contains(new ExternalIdentity(identityNamespace, value)))
            .ToArray();
    }

    private static string TitleOr(string? title, string fallback) =>
        string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
}
