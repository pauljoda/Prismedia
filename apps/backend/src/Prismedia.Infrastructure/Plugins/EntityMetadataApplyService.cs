using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Application.Security;
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
        "popularity",
        EntityStatCodes.Pages
    };

    private readonly PrismediaDbContext _db;
    private readonly IEntityExternalIdentityStore _externalIdentities;
    private readonly IEntityProviderIdentityStore? _providerIdentities;
    private readonly IPluginIdentityRouter? _identityRouter;
    private readonly IEntityLifecycleMutationLease _lifecycle;
    private readonly PluginArtworkDownloader _artwork;
    private readonly IGridThumbnailService? _gridThumbnails;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IAcquisitionReleaseDateChangeHandler? _releaseDateChanges;
    private readonly EntityStructurePlacementValidator _structurePlacement;
    private readonly Prismedia.Application.Settings.SettingsService? _settings;

    // Tags this apply unlinked; checked for orphanhood after a successful save so an untag
    // removes its now-unreferenced tag immediately instead of waiting for the deep scan net.
    private readonly HashSet<Guid> _removedTagCandidateIds = [];

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
        IEntityLifecycleMutationLease? lifecycle = null,
        ICurrentUserContext? currentUser = null,
        IAcquisitionReleaseDateChangeHandler? releaseDateChanges = null,
        Prismedia.Application.Settings.SettingsService? settings = null) {
        _db = db;
        _settings = settings;
        _externalIdentities = externalIdentities ?? new EfEntityExternalIdentityStore(db, TimeProvider.System);
        _providerIdentities = providerIdentities;
        _identityRouter = identityRouter;
        _lifecycle = lifecycle ?? new EfEntityLifecycleMutationLease(
            db,
            new EfEntityHierarchyReader(db));
        _artwork = new PluginArtworkDownloader(db, options, http);
        _gridThumbnails = gridThumbnails;
        _currentUser = currentUser;
        _releaseDateChanges = releaseDateChanges;
        _structurePlacement = new EntityStructurePlacementValidator(db);
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

        if (entityIds.Count > 0) {
            await _gridThumbnails.EnsureManyAsync(entityIds, cancellationToken);
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
        _structurePlacement.Reset();

        var fields = EntityMetadataPatchValidator.NormalizeFieldSet(request.Fields);
        EntityMetadataPatchValidator.Validate(fields, request.Patch);

        if (!await CanEditCollectionAsync(entityId, cancellationToken)) {
            return EntityMetadataPatchResult.NotFound;
        }

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
            await RemoveOrphanedTagCandidatesAsync(cancellationToken);
            if (_releaseDateChanges is not null && fields.Contains(MetadataPatchField.Dates.ToCode())) {
                await _releaseDateChanges.HandleAsync(entityId, cancellationToken);
            }
        } else {
            _artwork.RollbackStagedWrites();
        }
        return accepted ? result : EntityMetadataPatchResult.NotFound;
    }

    /// <summary>
    /// Deletes tags this apply unlinked when nothing references them anymore and the
    /// remove-orphan-tags setting is on. One indexed existence check per affected tag keeps
    /// untagging tidy without the library-wide sweep, which now runs only on deep scans.
    /// </summary>
    private async Task RemoveOrphanedTagCandidatesAsync(CancellationToken cancellationToken) {
        if (_removedTagCandidateIds.Count == 0 || _settings is null) {
            return;
        }

        var candidateIds = _removedTagCandidateIds.ToArray();
        _removedTagCandidateIds.Clear();
        if (!await _settings.GetRemoveOrphanTagsAsync(cancellationToken)) {
            return;
        }

        var tagCode = EntityKind.Tag.ToCode();
        var links = _db.EntityRelationshipLinks;
        var orphans = await _db.Entities
            .Where(entity => candidateIds.Contains(entity.Id) &&
                entity.KindCode == tagCode &&
                !links.Any(link => link.TargetEntityId == entity.Id))
            .ToListAsync(cancellationToken);
        if (orphans.Count > 0) {
            _db.Entities.RemoveRange(orphans);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> CanEditCollectionAsync(Guid entityId, CancellationToken cancellationToken) {
        if (_currentUser is null || _currentUser.IsSystem) {
            return true;
        }

        var ownership = await _db.CollectionDetails.AsNoTracking()
            .Where(detail => detail.EntityId == entityId)
            .Select(detail => (Guid?)detail.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);
        return ownership is null || ownership == _currentUser.UserId;
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

        if (fields.Contains(MetadataPatchField.Images.ToCode()) && request.SelectedImages is not null) {
            await _artwork.DownloadSelectedImagesAsync(entityId, request.SelectedImages, now, cancellationToken);
        }

        if (request.Children is { Count: > 0 } || request.Relationships is { Count: > 0 }) {
            var relationshipFieldsApplied =
                fields.Contains(MetadataPatchField.Credits.ToCode()) || fields.Contains(MetadataPatchField.Studio.ToCode()) || fields.Contains(MetadataPatchField.Tags.ToCode());
            await ApplyChildNodesAsync(
                entity.Id,
                entity.KindCode.DecodeAs<EntityKind>(),
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
        if (fields.Contains(MetadataPatchField.Title.ToCode())) {
            entity.Title = patch.Title!.Trim();
        }

        if (fields.Contains(MetadataPatchField.Description.ToCode())) {
            await UpsertDescriptionAsync(entity.Id, patch.Description, now, cancellationToken);
        }

        if (fields.Contains(MetadataPatchField.ExternalIds.ToCode())) {
            await ReplaceExternalIdsAsync(entity.Id, patch.ExternalIds, patch.Urls, cancellationToken);
        }

        if (fields.Contains(MetadataPatchField.Urls.ToCode())) {
            await ReplaceUrlsAsync(entity.Id, patch.Urls, now, cancellationToken);
        }

        await ApplyScopedRelationshipFieldsAsync(entity, fields, patch, now, cancellationToken);

        if (fields.Contains(MetadataPatchField.Dates.ToCode())) {
            await ReplaceDatesAsync(
                entity.Id,
                EntityMetadataDateNormalization.Normalize(patch),
                now,
                cancellationToken);
        }

        if (fields.Contains(MetadataPatchField.Stats.ToCode())) {
            await ReplaceStatsAsync(entity.Id, patch.Stats, now, cancellationToken);
        }

        if (fields.Contains(MetadataPatchField.Positions.ToCode())) {
            await ReplacePositionsAsync(entity, EntityMetadataPositionRules.Normalize(patch.Positions), now, cancellationToken);
        }

        if (fields.Contains(MetadataPatchField.Classification.ToCode())) {
            await ReplaceClassificationAsync(entity.Id, patch.Classification, now, cancellationToken);
        }

        if (fields.Contains(MetadataPatchField.Flags.ToCode())) {
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
        _structurePlacement.Reset();
        proposal = EntityMetadataProposalIdentityPolicy.RemoveSharedStructuralIdentities(proposal);

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
            if (_releaseDateChanges is not null
                && selectedFields.Contains(MetadataPatchField.Dates.ToCode(), StringComparer.OrdinalIgnoreCase)) {
                await _releaseDateChanges.HandleAsync(entityId, cancellationToken);
            }
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

        if (selected.Contains(MetadataPatchField.Title.ToCode()) && !string.IsNullOrWhiteSpace(patch.Title)) {
            entity.Title = patch.Title.Trim();
        }

        if (selected.Contains(MetadataPatchField.Description.ToCode())) {
            await UpsertDescriptionAsync(entityId, patch.Description, now, cancellationToken);
        }

        if (selected.Contains(MetadataPatchField.ExternalIds.ToCode())) {
            await UpsertExternalIdsAsync(entityId, patch.ExternalIds, patch.Urls, cancellationToken);
        }
        await BindProviderIdentityAsync(
            entity,
            proposal.Provider,
            patch.ExternalIds,
            cancellationToken);

        if (selected.Contains(MetadataPatchField.Urls.ToCode())) {
            await UpsertUrlsAsync(entityId, patch.Urls, now, cancellationToken);
        }

        // NSFW providers (e.g. Stash community scrapers) force every entity they touch — the
        // root, its credited people, studio, and tags — to be marked NSFW.
        var markNsfw = patch.Flags?.IsNsfw == true;
        await ApplySelectedRelationshipFieldsAsync(entity, selected, patch, now, markNsfw, cancellationToken);

        if (selected.Contains(MetadataPatchField.Dates.ToCode())) {
            await UpsertDatesAsync(
                entityId,
                EntityMetadataDateNormalization.Normalize(patch),
                now,
                cancellationToken);
        }

        if (selected.Contains(MetadataPatchField.Stats.ToCode())) {
            await UpsertStatsAsync(entityId, patch.Stats, now, cancellationToken);
        }

        if (selected.Contains(MetadataPatchField.Positions.ToCode())) {
            var normalizedPositions = EntityMetadataPositionRules.Normalize(patch.Positions);
            await UpsertPositionsAsync(entity, normalizedPositions, now, cancellationToken);
        }

        if (selected.Contains(MetadataPatchField.Classification.ToCode())) {
            await UpsertClassificationAsync(entityId, patch.Classification, now, cancellationToken);
        }

        if (selected.Contains(MetadataPatchField.Images.ToCode()) && selectedImages is not null) {
            await _artwork.DownloadSelectedImagesAsync(entityId, selectedImages, now, cancellationToken);
        }

        if (patch.Flags?.IsNsfw == true) {
            await UpsertFlagsAsync(entityId, new EntityMetadataFlagsPatch(null, true, null), now, cancellationToken);
        }

        // Walk the root's related entities and structural children through the single recursive node
        // applier. Relationship proposals only enrich entities the root's credit/studio/tags fields
        // linked, so gate them on that selection (the scalar fields were applied just above).
        var rootRelationshipFieldsApplied =
            selected.Contains(MetadataPatchField.Credits.ToCode()) || selected.Contains(MetadataPatchField.Studio.ToCode()) || selected.Contains(MetadataPatchField.Tags.ToCode());
        await ApplyChildNodesAsync(
            entity.Id,
            entity.KindCode.DecodeAs<EntityKind>(),
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
