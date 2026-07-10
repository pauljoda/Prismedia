using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Filesystem settings for artwork downloaded while applying plugin metadata.
/// </summary>
/// <param name="CacheRoot">Physical cache root served by the API under /assets.</param>
public sealed record PluginArtworkServiceOptions(string CacheRoot);

/// <summary>
/// Applies selected plugin metadata proposals into entity capability rows.
/// </summary>
public sealed partial class EntityMetadataApplyService : IEntityMetadataPatchService {
    // Stat codes are an open provider vocabulary (plugins may send any code; rows are
    // stored and displayed as-is), so this filter matches wire strings rather than a
    // closed [Code] enum. prism-vocab: external
    private static readonly HashSet<string> IgnoredStatCodes = new(StringComparer.OrdinalIgnoreCase) {
        "popularity"
    };

    private readonly PrismediaDbContext _db;
    private readonly IEntityExternalIdentityStore _externalIdentities;
    private readonly IEntityProviderIdentityStore? _providerIdentities;
    private readonly IPluginIdentityRouter? _identityRouter;
    private readonly IEntityLifecycleMutationLease _lifecycle;
    private readonly PluginArtworkDownloader _artwork;
    private readonly IGridThumbnailService? _gridThumbnails;

    /// <summary>
    /// Creates an apply service over EF Core rows and optional artwork downloading.
    /// </summary>
    /// <param name="db">Database context that owns entity capability tables.</param>
    /// <param name="options">Filesystem settings for downloaded artwork.</param>
    /// <param name="http">Optional HTTP client for tests or configured hosts.</param>
    /// <param name="gridThumbnails">
    /// Optional grid-thumbnail generator; when present, entities that receive artwork get
    /// their grid-card cover variants regenerated as part of the apply.
    /// </param>
    /// <param name="externalIdentities">
    /// Canonical external-identity resolver and writer. The optional fallback preserves direct
    /// construction in older integration tests while production supplies the scoped store.
    /// </param>
    public EntityMetadataApplyService(
        PrismediaDbContext db,
        PluginArtworkServiceOptions options,
        HttpClient? http = null,
        IGridThumbnailService? gridThumbnails = null,
        IEntityExternalIdentityStore? externalIdentities = null,
        IEntityProviderIdentityStore? providerIdentities = null,
        IPluginIdentityRouter? identityRouter = null,
        IEntityLifecycleMutationLease? lifecycle = null) {
        _db = db;
        _externalIdentities = externalIdentities ?? new EfEntityExternalIdentityStore(db, TimeProvider.System);
        _providerIdentities = providerIdentities;
        _identityRouter = identityRouter;
        _lifecycle = lifecycle ?? new EfEntityLifecycleMutationLease(
            db,
            new EfEntityHierarchyReader(db));
        _artwork = new PluginArtworkDownloader(db, options, http);
        _gridThumbnails = gridThumbnails;
    }

    /// <summary>
    /// Regenerates grid-card cover variants for every entity that received artwork during
    /// this apply. Runs after <see cref="PrismediaDbContext.SaveChangesAsync(CancellationToken)"/>
    /// so the variants derive from the committed covers rather than tracked-but-unsaved rows.
    /// </summary>
    private async Task RefreshGridThumbnailsForDownloadedArtworkAsync(CancellationToken cancellationToken) {
        var entityIds = _artwork.DrainArtworkEntityIds();
        if (_gridThumbnails is null) {
            return;
        }

        foreach (var entityId in entityIds) {
            await _gridThumbnails.EnsureAsync(entityId, cancellationToken);
        }
    }

    /// <summary>
    /// Applies a user-authored metadata patch to one entity. Only explicitly scoped fields
    /// are mutated, allowing callers to replace or clear individual editable sections without
    /// sending the entire entity shape.
    /// </summary>
    /// <param name="entityId">Entity receiving the patch.</param>
    /// <param name="request">Scoped metadata update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the entity exists and was updated; false when no active entity exists.</returns>
    public async Task<bool> ApplyPatchAsync(
        Guid entityId,
        EntityMetadataUpdateRequest request,
        CancellationToken cancellationToken) =>
        await ApplyPatchAsync(entityId, request, expectedKind: null, cancellationToken) == EntityMetadataPatchResult.Applied;

    /// <inheritdoc />
    public async Task<EntityMetadataPatchResult> ApplyPatchAsync(
        Guid entityId,
        EntityMetadataUpdateRequest request,
        string? expectedKind,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Patch);

        var fields = EntityMetadataPatchValidator.NormalizeFieldSet(request.Fields);
        EntityMetadataPatchValidator.Validate(fields, request.Patch);

        await _artwork.StageAsync(
            (request.SelectedImages?.Values ?? [])
                .Concat(ProposalArtworkUrls(request.Children ?? []))
                .Concat(ProposalArtworkUrls(request.Relationships ?? [])),
            cancellationToken);

        var result = EntityMetadataPatchResult.NotFound;
        bool accepted;
        try {
            accepted = await _lifecycle.ExecuteAsync(
                entityId,
                async leaseCancellationToken => result = await ApplyPatchWithinLifecycleAsync(
                    entityId,
                    request,
                    expectedKind,
                    fields,
                    leaseCancellationToken),
                cancellationToken);
        } catch {
            _artwork.RollbackStagedWrites();
            throw;
        }
        if (accepted && result == EntityMetadataPatchResult.Applied) {
            _artwork.CommitStagedWrites();
            await RefreshGridThumbnailsForDownloadedArtworkAsync(cancellationToken);
        } else {
            _artwork.RollbackStagedWrites();
        }
        return accepted ? result : EntityMetadataPatchResult.NotFound;
    }

    private async Task<EntityMetadataPatchResult> ApplyPatchWithinLifecycleAsync(
        Guid entityId,
        EntityMetadataUpdateRequest request,
        string? expectedKind,
        ISet<string> fields,
        CancellationToken cancellationToken) {

        var entity = await _db.Entities
            .FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null) {
            return EntityMetadataPatchResult.NotFound;
        }

        if (!string.IsNullOrWhiteSpace(expectedKind) &&
            !IsKindCompatible(entity.KindCode, expectedKind)) {
            return EntityMetadataPatchResult.KindMismatch;
        }

        var now = DateTimeOffset.UtcNow;
        await ApplyScopedPatchToEntityAsync(entity, fields, request.Patch, now, cancellationToken);

        if (fields.Contains("images") && request.SelectedImages is not null) {
            await _artwork.DownloadSelectedImagesAsync(entityId, request.SelectedImages, now, cancellationToken);
        }

        if (request.Children is { Count: > 0 } || request.Relationships is { Count: > 0 }) {
            var relationshipFieldsApplied =
                fields.Contains("credits") || fields.Contains("studio") || fields.Contains("tags");
            await ApplyChildNodesAsync(
                entity.Id,
                request.Children ?? [],
                request.Relationships ?? [],
                relationshipFieldsApplied,
                now,
                [entity.Id],
                [],
                progress: null,
                identifyEligibility: null,
                cancellationToken);
        }

        entity.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return EntityMetadataPatchResult.Applied;
    }

    private async Task ApplyScopedPatchToEntityAsync(
        EntityRow entity,
        ISet<string> fields,
        EntityMetadataPatch patch,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (fields.Contains("title")) {
            entity.Title = patch.Title!.Trim();
        }

        if (fields.Contains("description")) {
            await UpsertDescriptionAsync(entity.Id, patch.Description, now, cancellationToken);
        }

        if (fields.Contains("externalIds")) {
            await ReplaceExternalIdsAsync(entity.Id, patch.ExternalIds, patch.Urls, cancellationToken);
        }

        if (fields.Contains("urls")) {
            await ReplaceUrlsAsync(entity.Id, patch.Urls, now, cancellationToken);
        }

        await ApplyScopedRelationshipFieldsAsync(entity, fields, patch, now, cancellationToken);

        if (fields.Contains("dates")) {
            await ReplaceDatesAsync(entity.Id, patch.Dates, now, cancellationToken);
        }

        if (fields.Contains("stats")) {
            await ReplaceStatsAsync(entity.Id, patch.Stats, now, cancellationToken);
        }

        if (fields.Contains("positions")) {
            await ReplacePositionsAsync(entity, EntityMetadataPositionRules.Normalize(patch.Positions), now, cancellationToken);
        }

        if (fields.Contains("classification")) {
            await ReplaceClassificationAsync(entity.Id, patch.Classification, now, cancellationToken);
        }

        if (fields.Contains("flags")) {
            await UpsertFlagsAsync(entity.Id, patch.Flags, now, cancellationToken);
        }
    }

    /// <summary>
    /// Applies selected fields from a proposal to an existing entity.
    /// </summary>
    /// <param name="entityId">Entity receiving metadata.</param>
    /// <param name="proposal">Plugin proposal chosen by the user.</param>
    /// <param name="selectedFields">Field keys selected in the review UI.</param>
    /// <param name="selectedImages">Optional role-to-remote-URL artwork selections.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the entity exists and was updated.</returns>
    public async Task<bool> ApplyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        CancellationToken cancellationToken) =>
        await ApplyAsync(entityId, proposal, selectedFields, selectedImages, null, cancellationToken);

    /// <summary>
    /// Applies selected fields from a proposal to an existing entity and reports entity-level progress.
    /// </summary>
    /// <param name="entityId">Entity receiving metadata.</param>
    /// <param name="proposal">Plugin proposal chosen by the user.</param>
    /// <param name="selectedFields">Field keys selected in the review UI.</param>
    /// <param name="selectedImages">Optional role-to-remote-URL artwork selections.</param>
    /// <param name="progress">Optional progress reporter for synchronous queue accepts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the entity exists and was updated.</returns>
    public Task<bool> ApplyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        IIdentifyApplyProgressReporter? progress,
        CancellationToken cancellationToken) =>
        ApplyAsyncCore(
            entityId,
            proposal,
            selectedFields,
            selectedImages,
            progress,
            identifyEligibility: null,
            cancellationToken);

    /// <summary>
    /// Applies a prepared Identify proposal with a final persisted-child eligibility guard. Request
    /// metadata imports use the public apply overload and intentionally do not opt into this rule.
    /// </summary>
    internal Task<bool> ApplyIdentifyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        IIdentifyApplyProgressReporter? progress,
        IIdentifyTargetEligibilityService identifyEligibility,
        CancellationToken cancellationToken) =>
        ApplyAsyncCore(
            entityId,
            proposal,
            selectedFields,
            selectedImages,
            progress,
            identifyEligibility,
            cancellationToken);

    private async Task<bool> ApplyAsyncCore(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        IIdentifyApplyProgressReporter? progress,
        IIdentifyTargetEligibilityService? identifyEligibility,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(selectedFields);

        await _artwork.StageAsync(
            (selectedImages?.Values ?? [])
                .Concat(ProposalArtworkUrls([proposal])),
            cancellationToken);

        var applied = false;
        bool accepted;
        try {
            accepted = await _lifecycle.ExecuteAsync(
                entityId,
                async leaseCancellationToken => applied = await ApplyWithinLifecycleAsync(
                    entityId,
                    proposal,
                    selectedFields,
                    selectedImages,
                    progress,
                    identifyEligibility,
                    leaseCancellationToken),
                cancellationToken);
        } catch {
            _artwork.RollbackStagedWrites();
            throw;
        }
        if (accepted && applied) {
            _artwork.CommitStagedWrites();
            await RefreshGridThumbnailsForDownloadedArtworkAsync(cancellationToken);
        } else {
            _artwork.RollbackStagedWrites();
        }
        return accepted && applied;
    }

    private async Task<bool> ApplyWithinLifecycleAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        IIdentifyApplyProgressReporter? progress,
        IIdentifyTargetEligibilityService? identifyEligibility,
        CancellationToken cancellationToken) {

        var entity = await _db.Entities
            .FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null) {
            return false;
        }

        var selected = selectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var patch = proposal.Patch;
        var now = DateTimeOffset.UtcNow;
        var rootTitle = !string.IsNullOrWhiteSpace(patch.Title) ? patch.Title.Trim() : entity.Title;
        var rootPath = new[] { rootTitle };
        await ReportApplyProgressAsync(progress, entity.KindCode.DecodeAs<EntityKind>(), rootTitle, rootPath, cancellationToken);

        if (selected.Contains("title") && !string.IsNullOrWhiteSpace(patch.Title)) {
            entity.Title = patch.Title.Trim();
        }

        if (selected.Contains("description")) {
            await UpsertDescriptionAsync(entityId, patch.Description, now, cancellationToken);
        }

        if (selected.Contains("externalIds")) {
            await UpsertExternalIdsAsync(entityId, patch.ExternalIds, patch.Urls, cancellationToken);
        }
        await BindProviderIdentityAsync(
            entity,
            proposal.Provider,
            patch.ExternalIds,
            cancellationToken);

        if (selected.Contains("urls")) {
            await UpsertUrlsAsync(entityId, patch.Urls, now, cancellationToken);
        }

        // NSFW providers (e.g. Stash community scrapers) force every entity they touch — the
        // root, its credited people, studio, and tags — to be marked NSFW.
        var markNsfw = patch.Flags?.IsNsfw == true;
        await ApplySelectedRelationshipFieldsAsync(entity, selected, patch, now, markNsfw, cancellationToken);

        if (selected.Contains("dates")) {
            await UpsertDatesAsync(entityId, patch.Dates, now, cancellationToken);
        }

        if (selected.Contains("stats")) {
            await UpsertStatsAsync(entityId, patch.Stats, now, cancellationToken);
        }

        if (selected.Contains("positions")) {
            var normalizedPositions = EntityMetadataPositionRules.Normalize(patch.Positions);
            await UpsertPositionsAsync(entity, normalizedPositions, now, cancellationToken);
        }

        if (selected.Contains("classification")) {
            await UpsertClassificationAsync(entityId, patch.Classification, now, cancellationToken);
        }

        if (selected.Contains("images") && selectedImages is not null) {
            await _artwork.DownloadSelectedImagesAsync(entityId, selectedImages, now, cancellationToken);
        }

        if (patch.Flags?.IsNsfw == true) {
            await UpsertFlagsAsync(entityId, new EntityMetadataFlagsPatch(null, true, null), now, cancellationToken);
        }

        // Walk the root's related entities and structural children through the single recursive node
        // applier. Relationship proposals only enrich entities the root's credit/studio/tags fields
        // linked, so gate them on that selection (the scalar fields were applied just above).
        var rootRelationshipFieldsApplied =
            selected.Contains("credits") || selected.Contains("studio") || selected.Contains("tags");
        await ApplyChildNodesAsync(
            entity.Id,
            EntityMetadataProposalTraversal.StructuralChildren(proposal),
            EntityMetadataProposalTraversal.Relationships(proposal),
            rootRelationshipFieldsApplied,
            now,
            [entity.Id],
            rootPath,
            progress,
            identifyEligibility,
            cancellationToken);

        entity.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IEnumerable<string?> ProposalArtworkUrls(
        IEnumerable<EntityMetadataProposal> proposals) {
        var pending = new Stack<EntityMetadataProposal>(proposals.Reverse());
        while (pending.TryPop(out var proposal)) {
            foreach (var image in proposal.Images) {
                yield return image.Url;
            }
            foreach (var child in EntityMetadataProposalTraversal.StructuralChildren(proposal).Reverse()) {
                pending.Push(child);
            }
            foreach (var relationship in EntityMetadataProposalTraversal.Relationships(proposal).Reverse()) {
                pending.Push(relationship);
            }
        }
    }

}
