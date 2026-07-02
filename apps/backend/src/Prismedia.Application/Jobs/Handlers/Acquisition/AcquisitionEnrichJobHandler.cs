using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Enriches a request's held metadata from its originating metadata plugin: resolves the cover, fuller
/// description, and dates by the provider work-id (which the lightweight request-time search result often
/// lacks) and fills only the gaps. Best-effort — a provider miss or error leaves the held metadata as-is and
/// never disturbs the acquisition's state machine. The deeper, authoritative metadata pass (and children)
/// still runs at import via auto-identify.
/// </summary>
public sealed class AcquisitionEnrichJobHandler(
    IAcquisitionStore acquisitions,
    IRequestMetadataEnricher enricher,
    ILogger<AcquisitionEnrichJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionEnrich;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        var import = await acquisitions.GetImportContextAsync(payload.AcquisitionId, cancellationToken);
        if (import is null || string.IsNullOrWhiteSpace(import.PluginId) || string.IsNullOrWhiteSpace(import.PluginItemId)) {
            return; // nothing to enrich from
        }

        RequestMetadataEnrichment? enrichment;
        try {
            // Conservative SFW default: this background pass has no user session, and the request already
            // captured whatever the (already SFW-gated) search returned — so never pull NSFW-unrestricted
            // results here. An NSFW-flagged provider is skipped by the enricher.
            enrichment = await enricher.LookupByIdAsync(import.Kind, import.PluginId, import.PluginItemId, hideNsfw: true, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionEnrich: provider lookup failed for acquisition {Id}; leaving held metadata as-is.", payload.AcquisitionId);
            return;
        }

        if (enrichment is null) {
            return;
        }

        await acquisitions.EnrichMetadataAsync(payload.AcquisitionId, enrichment.Description, enrichment.PosterUrl, enrichment.Year, cancellationToken);
        await context.ReportProgressAsync(100, "Metadata enriched", cancellationToken);
    }
}
