using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

public sealed partial class AcquisitionService {
    /// <summary>
    /// Re-runs the release search for an existing acquisition on demand (the manual counterpart to monitoring).
    /// Enqueues the standard <see cref="JobType.AcquisitionSearch"/> — deduped per acquisition, and the handler
    /// re-checks that the acquisition is still searchable — so it can't disturb an in-flight grab. An explicit
    /// user action may revive Cancelled by claiming Searching before enqueue; stale queued jobs cannot. Returns
    /// the acquisition, or null when it no longer exists.
    /// </summary>
    public async Task<AcquisitionDetail?> ReSearchAsync(
        Guid id,
        CancellationToken cancellationToken,
        string? customQuery = null) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return null;
        }

        var explicitRevival = detail.Summary.Status == AcquisitionStatus.Cancelled;
        return await ScheduleSearchAsync(
            detail,
            manualReview: true,
            explicitRevival,
            customQuery,
            parentContext: null,
            JobGraphOrigin.Interactive,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EnsureOpenEntitySearchAsync(
        Guid entityId,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) {
        return await EnsureOpenEntitySearchAsync(
            entityId,
            bookRendition,
            parentContext: null,
            JobGraphOrigin.Interactive,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EnsureOpenEntitySearchAsync(
        Guid entityId,
        BookRendition? bookRendition,
        JobContext? parentContext,
        JobGraphOrigin origin,
        CancellationToken cancellationToken) {
        var detail = (await store.ListForEntityAsync(entityId, cancellationToken))
            .FirstOrDefault(candidate =>
                candidate.Summary.BookRendition == bookRendition
                && candidate.Summary.Status is not AcquisitionStatus.Imported and not AcquisitionStatus.Cancelled);
        if (detail is null) {
            return false;
        }

        if (detail.Summary.Status is AcquisitionStatus.Pending or AcquisitionStatus.AwaitingSelection) {
            var refreshed = await ScheduleSearchAsync(
                detail,
                manualReview: false,
                explicitRevival: false,
                customQuery: null,
                parentContext,
                origin,
                cancellationToken);
            return refreshed?.Summary.Status is not AcquisitionStatus.Imported and not AcquisitionStatus.Cancelled;
        }

        return true;
    }

    /// <summary>Claims and publishes one search while preserving the caller's automatic/manual intent.</summary>
    private async Task<AcquisitionDetail?> ScheduleSearchAsync(
        AcquisitionDetail detail,
        bool manualReview,
        bool explicitRevival,
        string? customQuery,
        JobContext? parentContext,
        JobGraphOrigin origin,
        CancellationToken cancellationToken) {
        if (!explicitRevival && !AcquisitionSearchJobHandler.CanScheduleSearch(detail.Summary.Status)) {
            return detail;
        }

        await EnsureImportCheckpointCanBeSupersededAsync(detail, cancellationToken);
        if (!await store.TryTransitionStatusAsync(
                detail.Summary.Id,
                [detail.Summary.Status],
                AcquisitionStatus.Searching,
                null,
                cancellationToken)) {
            return await store.GetAsync(detail.Summary.Id, cancellationToken);
        }

        var resourceKey = await DeclareSearchResourceAsync(cancellationToken);
        var searchRequest = new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(
                    detail.Summary.Id,
                    manualReview: manualReview,
                    customQuery: customQuery),
                TargetEntityId: detail.Summary.Id.ToString(),
                TargetLabel: detail.Summary.Title,
                Origin: origin,
                GraphRootEntityKind: detail.Summary.Kind.ToCode(),
                GraphRootEntityId: detail.Summary.EntityId?.ToString(),
                ResourceKey: resourceKey);
        var searchJob = parentContext is null
            ? await queue.EnqueueAsync(searchRequest, cancellationToken)
            : await parentContext.EnqueueAsync(searchRequest, cancellationToken);
        if (searchJob.GraphId is { } graphId) {
            await store.SetJobGraphIdAsync(detail.Summary.Id, graphId, cancellationToken);
        }
        return await store.GetAsync(detail.Summary.Id, cancellationToken);
    }
    /// <summary>
    /// Persists a new acquisition and schedules its search. Entity-bound work holds the authoritative
    /// Entity/monitor lifecycle lease across persistence, Searching provenance, and queue publication.
    /// Unbound work preserves the ordinary acquisition-only path.
    /// </summary>
    public Task<AcquisitionSummary> CreateAndSearchAsync(
        AcquisitionCreateRequest request,
        CancellationToken cancellationToken) =>
        CreateAndSearchAsync(request, parentContext: null, JobGraphOrigin.Interactive, cancellationToken);

    /// <inheritdoc />
    public async Task<AcquisitionSummary> CreateAndSearchAsync(
        AcquisitionCreateRequest request,
        JobContext? parentContext,
        JobGraphOrigin origin,
        CancellationToken cancellationToken) {
        var metadata = CreateMetadata(request);
        if (metadata.EntityId is not { } entityId) {
            return await CreateAndSearchCoreAsync(metadata, parentContext, origin, cancellationToken);
        }

        AcquisitionSummary? summary = null;
        var accepted = await entityLifecycle.ExecuteAsync(
            entityId,
            async leaseCancellationToken => summary = await CreateAndSearchCoreAsync(
                metadata,
                parentContext,
                origin,
                leaseCancellationToken),
            cancellationToken);
        if (!accepted || summary is null) {
            throw EntityLifecycleConflict();
        }

        return summary;
    }

    /// <inheritdoc />
    public Task<AcquisitionSummary> CreateAndSearchWithinEntityLifecycleAsync(
        AcquisitionCreateRequest request,
        CancellationToken cancellationToken) =>
        CreateAndSearchCoreAsync(
            CreateMetadata(request),
            parentContext: null,
            JobGraphOrigin.Interactive,
            cancellationToken);

    private static AcquisitionMetadata CreateMetadata(AcquisitionCreateRequest request) {
        if (string.IsNullOrWhiteSpace(request.Title)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionInvalid, "A title is required to start an acquisition.");
        }

        var externalIdentity = CreateExternalIdentity(request);
        return new AcquisitionMetadata(
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim(),
            string.IsNullOrWhiteSpace(request.Series) ? null : request.Series.Trim(),
            request.Year,
            string.IsNullOrWhiteSpace(request.PosterUrl) ? null : request.PosterUrl.Trim(),
            externalIdentity,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.Kind,
            request.EntityId,
            request.ProfileId,
            request.TargetLibraryRootId,
            request.SeasonNumber,
            request.EpisodeNumber,
            request.VolumeNumber,
            request.Kind == EntityKind.Book
                ? request.BookRendition ?? BookRendition.Ebook
                : null);
    }

    private async Task<AcquisitionSummary> CreateAndSearchCoreAsync(
        AcquisitionMetadata metadata,
        JobContext? parentContext,
        JobGraphOrigin origin,
        CancellationToken cancellationToken) {
        var summary = await store.CreateAsync(metadata, cancellationToken);
        if (!await store.TryTransitionStatusAsync(
                summary.Id,
                [AcquisitionStatus.Pending],
                AcquisitionStatus.Searching,
                null,
                cancellationToken)) {
            throw LifecycleChangedConflict();
        }
        summary = summary with {
            Status = AcquisitionStatus.Searching,
            StatusMessage = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var resourceKey = await DeclareSearchResourceAsync(cancellationToken);
        var searchRequest = new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(summary.Id),
                TargetEntityId: summary.Id.ToString(),
                TargetLabel: summary.Title,
                Origin: origin,
                GraphRootEntityKind: summary.Kind.ToCode(),
                GraphRootEntityId: summary.EntityId?.ToString(),
                ResourceKey: resourceKey);
        var searchJob = parentContext is null
            ? await queue.EnqueueAsync(searchRequest, cancellationToken)
            : await parentContext.EnqueueAsync(searchRequest, cancellationToken);
        if (searchJob.GraphId is { } graphId) {
            await store.SetJobGraphIdAsync(summary.Id, graphId, cancellationToken);
            summary = summary with { JobGraphId = graphId };
        }

        // When the request carries a persistent external identity, enrich the held metadata in the background
        // through the plugin registered for its namespace (cover, fuller description, dates the lightweight
        // search result lacked), so the acquisition surface fills in and the imported book can be seeded.
        // Best-effort — never blocks the request.
        if (metadata.ExternalIdentity is not null) {
            await queue.EnqueueChildAsync(
                searchJob,
                new EnqueueJobRequest(
                    JobType.AcquisitionEnrich,
                    PayloadJson: AcquisitionJobPayload.Serialize(summary.Id),
                    TargetEntityId: summary.Id.ToString(),
                    TargetLabel: summary.Title),
                cancellationToken);
        }

        return summary;
    }

    /// <summary>
    /// Materializes adapter-declared limits in the durable scheduler before publishing a node that needs
    /// them. Resource claims therefore happen before a worker or lane slot is occupied.
    /// </summary>
    private async Task<string?> DeclareSearchResourceAsync(CancellationToken cancellationToken) {
        if (searchResources is null || await searchResources.ResolveAsync(cancellationToken) is not { } requirement) {
            return null;
        }

        await queue.DeclareResourceAsync(
            requirement.Key,
            requirement.Policy.MaxConcurrency,
            requirement.Policy.MinimumStartInterval,
            cancellationToken);
        return requirement.Key;
    }

    private static AcquisitionConfigurationException EntityLifecycleConflict() =>
        new(
            ApiProblemCodes.AcquisitionInvalid,
            "This Entity is missing or still being changed by another cleanup operation. Refresh and retry the request.");

    private static ExternalIdentity? CreateExternalIdentity(AcquisitionCreateRequest request) {
        var hasNamespace = !string.IsNullOrWhiteSpace(request.IdentityNamespace);
        var hasValue = !string.IsNullOrWhiteSpace(request.IdentityValue);
        if (!hasNamespace && !hasValue) {
            return null;
        }

        if (!hasNamespace || !hasValue) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "An external identity requires both a namespace and a value.");
        }

        try {
            return new ExternalIdentity(request.IdentityNamespace!, request.IdentityValue!);
        } catch (ArgumentException exception) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionInvalid, exception.Message);
        }
    }
}
