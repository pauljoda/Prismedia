using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Auto-identifies a single scanned entity through the configured plugins and applies the first
/// confident match. Enqueued per entity by the library scan handlers when auto identify is enabled.
/// All provider selection, confidence gating, and apply logic lives in <see cref="IAutoIdentifyRunner"/>.
/// </summary>
public sealed class AutoIdentifyJobHandler(
    IAutoIdentifyRunner runner,
    ILogger<AutoIdentifyJobHandler> logger,
    TimeSpan? identifyTimeout = null) : IJobHandler {
    private static readonly TimeSpan DefaultIdentifyInactivityTimeout = TimeSpan.FromSeconds(90);
    private readonly TimeSpan _identifyInactivityTimeout = identifyTimeout ?? DefaultIdentifyInactivityTimeout;

    public JobType Type => JobType.AutoIdentify;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var entityId)) {
            logger.LogWarning("AutoIdentify: missing or invalid target entity id '{TargetEntityId}'", context.Job.TargetEntityId);
            return;
        }

        await context.ReportProgressAsync(10, "Identifying", cancellationToken);
        AutoIdentifyResult result;
        try {
            result = await runner.RunAsync(
                entityId,
                new AutoIdentifyRunOptions(
                    _identifyInactivityTimeout,
                    (progress, token) => context.ReportProgressAsync(
                        progress.Phase == AutoIdentifyProgressPhase.Applying
                            ? 90
                            : Math.Min(90, 10 + progress.ResolvedSteps),
                        progress.Phase == AutoIdentifyProgressPhase.Applying
                            ? FormatApplyProgressMessage(progress)
                            : $"Identifying children ({progress.RootChildCount} found)",
                        token)),
                cancellationToken);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new JobRetryLaterException(
                $"Auto identify made no progress for {_identifyInactivityTimeout.TotalSeconds:0} seconds.",
                TimeSpan.FromMinutes(1));
        }

        if (result.Applied) {
            logger.LogInformation(
                "AutoIdentify: applied {Provider} to entity {EntityId} (confidence {Confidence})",
                result.Provider, entityId, result.Confidence);
            await context.ReportProgressAsync(100, $"Applied {result.Provider}", cancellationToken);
        } else {
            logger.LogDebug(
                "AutoIdentify: no match applied for entity {EntityId} ({SkipReason})",
                entityId, result.SkipReason);
            await context.ReportProgressAsync(100, result.SkipReason ?? "No confident match", cancellationToken);
        }
    }

    private static string FormatApplyProgressMessage(AutoIdentifyProgress progress) =>
        string.IsNullOrWhiteSpace(progress.CurrentTitle)
            ? "Applying metadata"
            : $"Applying metadata: {progress.CurrentTitle}";
}
