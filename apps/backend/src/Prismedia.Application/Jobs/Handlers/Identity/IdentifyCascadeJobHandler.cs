using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Runs the background full-tree identify cascade for a queued entity. Enqueued by the identify queue
/// after a candidate is picked and the root proposal is seeded; the actual walk and streaming live in
/// <see cref="IIdentifyCascadeRunner"/>, which persists the growing proposal onto the queue item.
/// </summary>
public sealed class IdentifyCascadeJobHandler(
    IIdentifyCascadeRunner runner,
    ILogger<IdentifyCascadeJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.IdentifyCascade;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = IdentifyCascadePayload.Parse(context.Job.PayloadJson);

        await context.ReportProgressAsync(10, "Resolving children", cancellationToken);
        try {
            await runner.RunAsync(payload, context.Job.Id, cancellationToken);
            await context.ReportProgressAsync(100, "Children resolved", cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "IdentifyCascade: failed for entity {EntityId}", payload.EntityId);
            throw;
        }
    }
}
