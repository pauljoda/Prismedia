using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Applies a reviewed proposal as a required node in the originating interactive graph.</summary>
[JobDefinition(JobType.IdentifyApply)]
public sealed class IdentifyApplyJobHandler(
    IIdentifyApplyRunner runner,
    ILogger<IdentifyApplyJobHandler> logger) : IJobHandler {
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = IdentifyApplyPayload.Parse(context.Job.PayloadJson);
        var graphId = context.Job.GraphId
            ?? throw new InvalidOperationException("Identify apply requires an owning graph.");
        await context.ReportProgressAsync(5, "Applying reviewed metadata", cancellationToken);
        try {
            await runner.RunAsync(payload, graphId, context.Job.IsFinalAttempt, cancellationToken);
            await context.ReportProgressAsync(100, "Metadata applied", cancellationToken);
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            logger.LogWarning(exception, "Identify apply failed for entity {EntityId}", payload.EntityId);
            throw;
        }
    }
}
