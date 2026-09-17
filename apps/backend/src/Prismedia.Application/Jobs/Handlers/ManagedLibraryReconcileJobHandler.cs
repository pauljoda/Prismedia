using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>One finite refresh of an established connected holding, independently of queue-history retention.</summary>
[JobDefinition(JobType.ManagedLibraryReconcile, ResourceClass = JobResourceClass.StandardCpu)]
public sealed class ManagedLibraryReconcileJobHandler(ManagedTrackingService service, ManagedRequestProcessor requests) : IJobHandler {
    /// <inheritdoc />
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var id)) throw new ArgumentException("A tracked holding ID is required.");
        if (!await requests.ProcessAsync(id, cancellationToken)) await service.ReconcileAsync(id, cancellationToken);
    }
}
