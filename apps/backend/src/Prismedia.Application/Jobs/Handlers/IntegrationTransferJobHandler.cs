using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Resumes one durable integration transfer independently of transient queue history.</summary>
[JobDefinition(JobType.IntegrationTransfer, ResourceClass = JobResourceClass.StandardCpu)]
public sealed class IntegrationTransferJobHandler(IIntegrationTransferStore store, SourceTransferProcessor source,
    SourceAcquisitionProcessor sourceRequests, RemoteTransferProcessor executor) : IJobHandler {
    /// <inheritdoc />
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var id)) throw new ArgumentException("An integration transfer job needs its stable operation ID.");
        var work = await store.FindAsync(id, cancellationToken) ?? throw new IntegrationTransferNotFoundException();
        await (work.Transfer.State.Mode switch {
            IntegrationTransferMode.SourceDownload => source.ProcessAsync(id, context, cancellationToken),
            IntegrationTransferMode.SourceRequest => sourceRequests.ProcessAsync(id, context, cancellationToken),
            IntegrationTransferMode.RemoteExecutor => executor.ProcessAsync(id, context, cancellationToken),
            _ => throw new InvalidOperationException("Unknown transfer execution mode.")
        });
    }
}
