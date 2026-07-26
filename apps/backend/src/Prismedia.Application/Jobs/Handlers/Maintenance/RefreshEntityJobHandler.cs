using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>Plans exact entity processing for a user-requested refresh without enumerating a library root.</summary>
public sealed class RefreshEntityJobHandler(EntityProcessingGraphPlanner planner) : IJobHandler {
    public JobType Type => JobType.RefreshEntity;

    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) =>
        planner.PlanAsync(context, finalization: null, cancellationToken);
}
