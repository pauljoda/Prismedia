using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Resumes one durable integration transfer independently of transient queue history.</summary>
[JobDefinition(JobType.IntegrationTransfer, ResourceClass = JobResourceClass.StandardCpu)]
public sealed class IntegrationTransferJobHandler(SourceTransferProcessor processor) : IJobHandler {
    /// <inheritdoc />
    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) =>
        Guid.TryParse(context.Job.TargetEntityId, out var id)
            ? processor.ProcessAsync(id, context, cancellationToken)
            : throw new ArgumentException("An integration transfer job needs its stable operation ID.");
}
