namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable consumption capability shared by playable, readable, and viewable entities.
/// Resume position is time-based when present; access and active-duration state is media-agnostic.
/// </summary>
public sealed class CapabilityConsumption : EntityCapability {
    /// <summary>Creates a consumption capability.</summary>
    /// <param name="value">Initial consumption state.</param>
    public CapabilityConsumption(State? value = null) {
        Value = value ?? State.Empty;
    }

    /// <summary>One user's cached consumption totals for an entity.</summary>
    /// <param name="AccessCount">Number of times the entity was opened for consumption.</param>
    /// <param name="CompletionCount">Number of completed consumption events.</param>
    /// <param name="SkipCount">Number of quick-abandon events.</param>
    /// <param name="ActiveDuration">Total actively reported consumption duration.</param>
    /// <param name="ResumeTime">Time position where playback should resume, when applicable.</param>
    /// <param name="LastAccessedAt">Timestamp of the latest open/start event.</param>
    /// <param name="LastActiveAt">Timestamp of the latest accepted progress or activity signal.</param>
    /// <param name="CompletedAt">Timestamp when the entity most recently became complete.</param>
    public sealed record State(
        int AccessCount,
        int CompletionCount,
        int SkipCount,
        TimeSpan ActiveDuration,
        TimeSpan ResumeTime,
        DateTimeOffset? LastAccessedAt,
        DateTimeOffset? LastActiveAt,
        DateTimeOffset? CompletedAt) {
        /// <summary>Empty state for an entity that has never been consumed.</summary>
        public static State Empty { get; } = new(
            0,
            0,
            0,
            TimeSpan.Zero,
            TimeSpan.Zero,
            null,
            null,
            null);
    }

    /// <summary>Single-user consumption state.</summary>
    public State Value { get; private set; }

    /// <summary>Records one explicit open/start event.</summary>
    /// <param name="at">Timestamp of the access.</param>
    public void RecordAccessed(DateTimeOffset at) {
        var next = Value with { AccessCount = Value.AccessCount + 1 };
        if (AcceptsActivitySignal(at)) {
            next = next with { LastAccessedAt = at, LastActiveAt = at };
        }

        Value = next;
    }

    /// <summary>Records a time-based resume position from the latest activity signal.</summary>
    public void RecordResume(TimeSpan position, DateTimeOffset at) {
        if (!AcceptsActivitySignal(at)) {
            return;
        }

        Value = Value with {
            ResumeTime = position < TimeSpan.Zero ? TimeSpan.Zero : position,
            LastActiveAt = at
        };
    }

    /// <summary>Clears time-based resume and completion for an intentional fresh start.</summary>
    public void RecordStartOver(DateTimeOffset at) {
        if (!AcceptsActivitySignal(at)) {
            return;
        }

        Value = Value with {
            ResumeTime = TimeSpan.Zero,
            CompletedAt = null,
            LastActiveAt = at
        };
    }

    /// <summary>
    /// Records threshold completion. Repeated threshold signals are idempotent until the entity is
    /// reopened as incomplete.
    /// </summary>
    public void RecordCompleted(DateTimeOffset at) {
        if (!AcceptsActivitySignal(at)) {
            return;
        }

        Value = Value with {
            CompletionCount = Value.CompletedAt is null ? Value.CompletionCount + 1 : Value.CompletionCount,
            ResumeTime = TimeSpan.Zero,
            CompletedAt = at,
            LastActiveAt = at
        };
    }

    /// <summary>
    /// Records a discrete completed occurrence. Historical imports still advance the total but do
    /// not replace a newer resume/completion observation.
    /// </summary>
    public void RecordCompletedOccurrence(DateTimeOffset at) {
        var next = Value with { CompletionCount = Value.CompletionCount + 1 };
        if (AcceptsActivitySignal(at)) {
            next = next with {
                ResumeTime = TimeSpan.Zero,
                CompletedAt = at,
                LastActiveAt = at
            };
        }

        Value = next;
    }

    /// <summary>Records a likely skip without changing the current resume or completion state.</summary>
    public void RecordSkipped(DateTimeOffset at) {
        var next = Value with { SkipCount = Value.SkipCount + 1 };
        if (AcceptsActivitySignal(at)) {
            next = next with { LastActiveAt = at };
        }

        Value = next;
    }

    /// <summary>Marks the entity complete without changing its resume position.</summary>
    public void MarkCompleted(DateTimeOffset at) {
        if (!AcceptsActivitySignal(at)) {
            return;
        }

        Value = Value with {
            CompletionCount = Value.CompletedAt is null ? Value.CompletionCount + 1 : Value.CompletionCount,
            CompletedAt = at,
            LastActiveAt = at
        };
    }

    /// <summary>Clears completion without changing historical totals or resume.</summary>
    public void MarkIncomplete(DateTimeOffset at) {
        if (!AcceptsActivitySignal(at)) {
            return;
        }

        Value = Value with { CompletedAt = null, LastActiveAt = at };
    }

    /// <summary>Accumulates a bounded active interval into the cached total.</summary>
    public void AccumulateActiveDuration(TimeSpan delta, DateTimeOffset at) {
        if (delta <= TimeSpan.Zero) {
            return;
        }

        Value = Value with {
            ActiveDuration = Value.ActiveDuration + delta,
            LastActiveAt = AcceptsActivitySignal(at) ? at : Value.LastActiveAt
        };
    }

    private bool AcceptsActivitySignal(DateTimeOffset at) =>
        Value.LastActiveAt is null || at >= Value.LastActiveAt;
}
