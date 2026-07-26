using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>Adapts the durable identify apply handler to the queue state machine.</summary>
internal sealed class IdentifyApplyRunnerAdapter(IdentifyQueueService queueService) : IIdentifyApplyRunner {
    public Task RunAsync(
        IdentifyApplyPayload payload,
        Guid graphId,
        bool isFinalAttempt,
        CancellationToken cancellationToken) =>
        queueService.RunApplyAsync(payload, graphId, isFinalAttempt, cancellationToken);
}
