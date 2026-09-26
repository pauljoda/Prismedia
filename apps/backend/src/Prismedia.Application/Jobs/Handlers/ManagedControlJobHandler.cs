using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Runs one finite manager stage; durable action state handles recovery independently of queue retention.</summary>
[JobDefinition(JobType.ManagedControl, ResourceClass = JobResourceClass.StandardCpu)]
public sealed class ManagedControlJobHandler(ManagedControlProcessor processor) : IJobHandler {
    /// <inheritdoc />
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var id)) throw new ArgumentException("A manager action ID is required.");
        await processor.ProcessAsync(id, cancellationToken);
    }
}
