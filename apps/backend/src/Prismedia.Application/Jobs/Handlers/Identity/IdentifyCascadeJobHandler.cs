using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
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
            await runner.RunAsync(payload, context.Job.Id, context.Job.IsFinalAttempt, cancellationToken);
            await context.ReportProgressAsync(100, "Children resolved", cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) when (ProviderTransientErrors.IsRetryable(ex.Message)) {
            // A rate-limited or temporarily-down provider should defer the cascade (attempts
            // refunded) rather than burn one of its retries on an upstream that just needs time.
            logger.LogWarning(ex, "IdentifyCascade: provider temporarily unavailable for entity {EntityId}", payload.EntityId);
            throw new JobRetryLaterException(
                $"Identify cascade provider is temporarily unavailable: {ex.Message}",
                TimeSpan.FromMinutes(1));
        } catch (Exception ex) {
            logger.LogWarning(ex, "IdentifyCascade: failed for entity {EntityId}", payload.EntityId);
            throw;
        }
    }
}
