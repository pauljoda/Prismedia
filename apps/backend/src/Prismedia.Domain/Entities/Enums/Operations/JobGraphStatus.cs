namespace Prismedia.Domain.Entities;

/// <summary>Aggregate lifecycle of a durable job graph.</summary>
public enum JobGraphStatus {
    [Code("queued")]
    Queued,

    [Code("running")]
    Running,

    [Code("waiting")]
    Waiting,

    [Code("completed")]
    Completed,

    [Code("completed-with-warnings")]
    CompletedWithWarnings,

    [Code("failed")]
    Failed,

    [Code("cancelled")]
    Cancelled
}
