using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Job handler used to verify that the durable queue can claim, run, and complete a job.
/// </summary>
[JobDefinition(JobType.Noop)]
public class NoOpJobHandler : IJobHandler {

    /// <inheritdoc />
    public virtual Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}
