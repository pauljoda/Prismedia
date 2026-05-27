using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;

namespace Prismedia.Api.Endpoints;

internal static class JobMaintenanceEndpoints {
    internal static RouteGroupBuilder MapJobMaintenanceEndpoints(this RouteGroupBuilder group) {
        group.MapPost("/rebuild-previews", async (
            JobService jobs,
            CancellationToken cancellationToken) =>
            Results.Ok(await jobs.RebuildPreviewsAsync(cancellationToken)))
            .WithName("RebuildPreviews")
            .WithSummary("Queues preview generation for all media entities.")
            .Produces<BulkJobResponse>();

        group.MapPost("/backfill-fingerprints", async (
            JobService jobs,
            CancellationToken cancellationToken) =>
            Results.Ok(await jobs.BackfillFingerprintsAsync(cancellationToken)))
            .WithName("BackfillFingerprints")
            .WithSummary("Queues fingerprint generation for entities that lack one.")
            .Produces<BulkJobResponse>();

        return group;
    }
}
