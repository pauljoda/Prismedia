using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Enriches a request from its originating metadata plugin after the interactive commit: resolves the
/// cover, fuller description, and dates by persistent work identity and, for structural acquisition units,
/// materializes their child graph from the same provider response. Best-effort — a provider miss or error
/// leaves held descriptive metadata untouched; a successful release-date miss records that the date-entry
/// prompt is now appropriate while the request remains WaitingForRelease. A transient provider error remains retryable. Import still runs authoritative
/// auto-identify.
/// </summary>
public sealed class AcquisitionEnrichJobHandler(
    IAcquisitionStore acquisitions,
    IRequestMetadataEnricher enricher,
    IRequestChildHydrator childHydrator,
    ILogger<AcquisitionEnrichJobHandler> logger,
    IEntityMetadataPatchService? entityMetadata = null,
    IMonitorStore? monitors = null,
    IAcquisitionReleaseTimingService? releaseTiming = null) : IJobHandler {
    public JobType Type => JobType.AcquisitionEnrich;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        var import = await acquisitions.GetImportContextAsync(payload.AcquisitionId, cancellationToken);
        if (import is null) {
            return;
        }
        if (import.ExternalIdentity is not { } externalIdentity) {
            await CompleteReleaseTimingRefreshAsync(import, cancellationToken);
            return;
        }

        RequestMetadataEnrichment? enrichment;
        RequestChildHydrationResult? childHydration = null;
        try {
            // Conservative SFW default: this background pass has no user session, and the request already
            // captured whatever the (already SFW-gated) search returned — so never pull NSFW-unrestricted
            // results here. An NSFW-flagged provider is skipped by the enricher.
            if (import.EntityId is { } entityId) {
                childHydration = await childHydrator.HydrateAsync(
                    entityId,
                    hideNsfw: true,
                    cancellationToken);
            }

            enrichment = childHydration is null
                ? await enricher.LookupByIdAsync(
                    import.Kind,
                    externalIdentity,
                    hideNsfw: true,
                    cancellationToken)
                : childHydration.Enrichment;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionEnrich: provider lookup failed for acquisition {Id}; leaving held metadata as-is.", payload.AcquisitionId);
            return;
        }

        if (enrichment is null && childHydration is not { Hydrated: true }) {
            await CompleteReleaseTimingRefreshAsync(import, cancellationToken);
            return;
        }

        if (enrichment is not null) {
            await acquisitions.EnrichMetadataAsync(
                payload.AcquisitionId,
                enrichment.Description,
                enrichment.PosterUrl,
                enrichment.Year,
                cancellationToken);

            if (import.EntityId is { } entityId
                && entityMetadata is not null
                && enrichment.Patch is { } patch
                && (patch.DateEntries.Count > 0 || patch.Dates.Count > 0)) {
                await entityMetadata.ApplyPatchAsync(
                    entityId,
                    new EntityMetadataUpdateRequest(
                        [MetadataPatchField.Dates.ToCode()],
                        patch),
                    import.Kind.ToCode(),
                    cancellationToken);
            }
        }
        await CompleteReleaseTimingRefreshAsync(import, cancellationToken);
        await context.ReportProgressAsync(
            100,
            childHydration is { Hydrated: true } ? "Metadata and child graph enriched" : "Metadata enriched",
            cancellationToken);
    }

    /// <summary>
    /// Re-evaluates a release gate after one completed provider pass. A newly available or removed gate is
    /// made due for the monitor to claim; a still-missing configured date enables the explicit date prompt;
    /// a known future date remains on its normal low-frequency monitor cadence.
    /// </summary>
    private async Task CompleteReleaseTimingRefreshAsync(
        AcquisitionImportContext import,
        CancellationToken cancellationToken) {
        if (releaseTiming is null
            || await acquisitions.GetStatusAsync(import.Id, cancellationToken) is not (
                AcquisitionStatus.WaitingForRelease or AcquisitionStatus.ManualSearchRequired)) {
            return;
        }

        var timing = await releaseTiming.EvaluateAsync(
            import.EntityId,
            import.ProfileId,
            import.Kind,
            cancellationToken);
        if (timing.CanSearch) {
            await acquisitions.SetReleaseDateMetadataUnavailableAsync(
                import.Id,
                unavailable: false,
                message: null,
                cancellationToken);
            if (monitors is not null) {
                await monitors.MarkSearchDueByAcquisitionAsync(import.Id, cancellationToken);
            }
            return;
        }

        if (timing.WaitingForMetadata) {
            await acquisitions.SetReleaseDateMetadataUnavailableAsync(
                import.Id,
                unavailable: true,
                AcquisitionReleaseTimingService.ReleaseDateUnavailableMessage(timing.DateType),
                cancellationToken);
            return;
        }

        await acquisitions.SetReleaseDateMetadataUnavailableAsync(
            import.Id,
            unavailable: false,
            timing.Message,
            cancellationToken);
    }
}
