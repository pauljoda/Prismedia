using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>
/// Runs the canonical entity-scoped processing planner after an import or source revision. It shares
/// the refresh implementation so import engines never duplicate kind-specific downstream policy.
/// </summary>
public sealed class ReconcileEntityJobHandler(EntityProcessingGraphPlanner planner) : IJobHandler {
    public JobType Type => JobType.ReconcileEntity;

    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var finalization = context.Job.PayloadJson is { Length: > 2 }
            ? AcquisitionFinalizeJobPayload.Parse(context.Job.PayloadJson)
            : null;
        return planner.PlanAsync(context, finalization, cancellationToken);
    }
}
