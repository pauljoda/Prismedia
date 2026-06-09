using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Adapts <see cref="IdentifyQueueService"/> for use by the identify-cascade job handler. Each call
/// runs the entity's full child-tree cascade and streams the growing proposal onto the queue item.
/// </summary>
internal sealed class IdentifyCascadeRunnerAdapter(IdentifyQueueService queueService) : IIdentifyCascadeRunner {
    public Task RunAsync(IdentifyCascadePayload payload, Guid cascadeJobId, bool isFinalAttempt, CancellationToken cancellationToken) =>
        queueService.RunCascadeAsync(payload, cascadeJobId, isFinalAttempt, cancellationToken);
}
