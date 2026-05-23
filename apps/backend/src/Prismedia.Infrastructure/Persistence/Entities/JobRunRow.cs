using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class JobRunRow {
    public Guid Id { get; set; }

    public JobType Type { get; set; } = JobType.Noop;

    public JobRunStatus Status { get; set; } = JobRunStatus.Queued;

    public string PayloadJson { get; set; } = "{}";

    public int Priority { get; set; }

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 3;

    public int Progress { get; set; }

    public string? Message { get; set; }

    /// <summary>Entity kind for dashboard display and deduplication (e.g. "video", "library-root").</summary>
    public string? TargetEntityKind { get; set; }

    /// <summary>Entity identifier for dashboard display and deduplication.</summary>
    public string? TargetEntityId { get; set; }

    /// <summary>Human-readable label shown on the job dashboard.</summary>
    public string? TargetLabel { get; set; }

    public DateTimeOffset AvailableAt { get; set; }

    public DateTimeOffset? LockedAt { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
