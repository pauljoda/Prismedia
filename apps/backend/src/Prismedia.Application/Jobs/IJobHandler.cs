using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Application handler for executing one durable background job type.
/// Handlers are resolved per-scope so they can take scoped dependencies via constructor injection.
/// </summary>
public interface IJobHandler {
    /// <summary>
    /// Gets the job type handled by this implementation.
    /// </summary>
    JobType Type { get; }

    /// <summary>
    /// Executes the claimed job run. The context provides progress reporting and job chaining.
    /// </summary>
    /// <param name="context">Execution context with the job snapshot, progress, and enqueue access.</param>
    /// <param name="cancellationToken">Token used to cancel execution.</param>
    Task HandleAsync(JobContext context, CancellationToken cancellationToken);
}
