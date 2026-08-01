using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Executes a provider-backed identify expansion node for the owning entity graph. The payload remains
/// wire-compatible with historical cascades, while the distinct job type makes new graph work explicit
/// and leaves <see cref="JobType.IdentifyCascade"/> as a decode-only compatibility path.
/// </summary>
[JobDefinition(JobType.IdentifyProviderCall)]
public sealed class IdentifyProviderCallJobHandler(
    IIdentifyProviderCallRunner runner,
    ILogger<IdentifyProviderCallJobHandler> logger) : IJobHandler {
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = IdentifyProviderCallPayload.Parse(context.Job.PayloadJson);
        await context.ReportProgressAsync(10, "Resolving provider metadata", cancellationToken);
        try {
            await runner.RunAsync(payload, context, context.Job.IsFinalAttempt, cancellationToken);
            await context.ReportProgressAsync(100, "Provider metadata resolved", cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) when (ProviderTransientErrors.IsRetryable(ex.Message)) {
            logger.LogWarning(ex, "Identify provider call temporarily unavailable for entity {EntityId}", payload.TargetEntityId);
            throw new JobRetryLaterException(
                $"Identify provider is temporarily unavailable: {ex.Message}",
                TimeSpan.FromMinutes(1));
        } catch (Exception ex) {
            logger.LogWarning(ex, "Identify provider call failed for entity {EntityId}", payload.TargetEntityId);
            throw;
        }
    }
}
