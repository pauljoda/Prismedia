using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Publishes stable operation targets through the application's durable graph queue.</summary>
public sealed class IntegrationTransferScheduler(IJobQueueService queue) : IIntegrationTransferScheduler {
    /// <inheritdoc />
    public async Task EnqueueAsync(Guid operationId, string title, CancellationToken cancellationToken) {
        _ = await queue.EnqueueAsync(new EnqueueJobRequest(JobType.IntegrationTransfer, TargetEntityKind: JobTargetKinds.IntegrationTransfer,
            TargetEntityId: operationId.ToString(), TargetLabel: title), cancellationToken);
        await queue.WakeTargetAsync(JobType.IntegrationTransfer, operationId.ToString(), cancellationToken);
    }
}
