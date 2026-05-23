using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Job handler used to verify that the durable queue can claim, run, and complete a job.
/// </summary>
public sealed class NoOpJobHandler : IJobHandler {
    /// <inheritdoc />
    public JobType Type => JobType.Noop;

    /// <inheritdoc />
    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}
