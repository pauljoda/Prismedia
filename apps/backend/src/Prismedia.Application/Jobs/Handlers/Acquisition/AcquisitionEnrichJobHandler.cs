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
/// leaves held metadata and acquisition state untouched; import still runs authoritative auto-identify.
/// </summary>
public sealed class AcquisitionEnrichJobHandler(
    IAcquisitionStore acquisitions,
    IRequestMetadataEnricher enricher,
    IRequestChildHydrator childHydrator,
    ILogger<AcquisitionEnrichJobHandler> logger,
    IEntityMetadataPatchService? entityMetadata = null,
    IMonitorStore? monitors = null) : IJobHandler {
    public JobType Type => JobType.AcquisitionEnrich;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        var import = await acquisitions.GetImportContextAsync(payload.AcquisitionId, cancellationToken);
        if (import?.ExternalIdentity is not { } externalIdentity) {
            return; // nothing to enrich from
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
                var result = await entityMetadata.ApplyPatchAsync(
                    entityId,
                    new EntityMetadataUpdateRequest(
                        [MetadataPatchField.Dates.ToCode()],
                        patch),
                    import.Kind.ToCode(),
                    cancellationToken);
                if (result == EntityMetadataPatchResult.Applied && monitors is not null) {
                    await monitors.MarkSearchDueByAcquisitionAsync(
                        payload.AcquisitionId,
                        cancellationToken);
                }
            }
        }
        await context.ReportProgressAsync(
            100,
            childHydration is { Hydrated: true } ? "Metadata and child graph enriched" : "Metadata enriched",
            cancellationToken);
    }
}
