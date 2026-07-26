using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Runs one requested identify provider search for a single queue item. Enqueued by the identify
/// queue whenever the user requests a search (single item, bulk selection, candidate pick, or a
/// manual query); the state transitions and provider walk live in <see cref="IIdentifySearchRunner"/>.
/// Shares the process-wide identify provider slot with auto identify, entering on the interactive
/// side so background work yields the slot to it.
/// </summary>
public sealed class IdentifySearchJobHandler(
    IIdentifySearchRunner runner,
    AutoIdentifyConcurrencyGate gate,
    ILogger<IdentifySearchJobHandler> logger,
    TimeSpan? identifyTimeout = null) : IJobHandler {
    private static readonly TimeSpan DefaultIdentifyTimeout = TimeSpan.FromSeconds(90);
    private readonly TimeSpan _identifyTimeout = identifyTimeout ?? DefaultIdentifyTimeout;

    public JobType Type => JobType.IdentifySearch;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = IdentifySearchPayload.Parse(context.Job.PayloadJson);

        using var lease = payload.Provider is null
            ? gate.TryEnterInteractive()
                ?? throw new JobRetryLaterException("Unscoped identify search slot busy.", TimeSpan.FromSeconds(5))
            : null;

        await context.ReportProgressAsync(10, "Searching", cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_identifyTimeout);
        try {
            await runner.RunAsync(payload, context.Job.Id, context.Job.IsFinalAttempt, timeout.Token);
            await context.ReportProgressAsync(100, "Search finished", cancellationToken);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new JobRetryLaterException(
                $"Identify search timed out after {_identifyTimeout.TotalSeconds:0} seconds.",
                TimeSpan.FromMinutes(1));
        } catch (JobRetryLaterException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "IdentifySearch: failed for entity {EntityId}", payload.EntityId);
            throw;
        }
    }
}
