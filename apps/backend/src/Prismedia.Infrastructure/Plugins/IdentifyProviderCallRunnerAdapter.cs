using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>Adapts the identify queue state machine to one graph-scoped provider-call handler.</summary>
internal sealed class IdentifyProviderCallRunnerAdapter(IdentifyQueueService queueService) : IIdentifyProviderCallRunner {
    public Task RunAsync(
        IdentifyProviderCallPayload payload,
        JobContext context,
        bool isFinalAttempt,
        CancellationToken cancellationToken) =>
        queueService.RunProviderCallAsync(payload, context, isFinalAttempt, cancellationToken);
}
