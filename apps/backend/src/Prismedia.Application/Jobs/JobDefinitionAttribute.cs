using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Declares the durable queue policy and dispatch identity owned by an <see cref="IJobHandler"/>.
/// A handler may declare more than one job type when its execution is intentionally identical.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class JobDefinitionAttribute(JobType type) : Attribute {
    /// <summary>Gets the durable job type dispatched to the annotated handler.</summary>
    public JobType Type { get; } = type;

    /// <summary>Gets the default CPU resource class. Light work is the normal default.</summary>
    public JobResourceClass ResourceClass { get; init; } = JobResourceClass.Light;

    /// <summary>Gets the graph completion importance. Required work is the normal default.</summary>
    public JobNodeImportance Importance { get; init; } = JobNodeImportance.Required;

    /// <summary>Gets the queue-wide singleton behavior. Jobs are non-singletons by default.</summary>
    public JobSingletonBehavior SingletonBehavior { get; init; } = JobSingletonBehavior.None;

    /// <summary>
    /// Gets whether queued or running work of this type must drain before background Auto Identify starts.
    /// </summary>
    public bool BlocksAutoIdentify { get; init; }
}

/// <summary>Describes whether a job type has queue-wide singleton behavior.</summary>
public enum JobSingletonBehavior {
    /// <summary>Each enqueue is independently eligible for normal de-duplication.</summary>
    None,

    /// <summary>Only one queued or running job of this type may exist across the queue.</summary>
    QueueWide,

    /// <summary>
    /// Only untargeted jobs are queue-wide singletons; explicitly targeted work remains independent.
    /// </summary>
    QueueWideWhenUntargeted
}
