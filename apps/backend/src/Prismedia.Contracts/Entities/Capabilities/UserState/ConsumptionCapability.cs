namespace Prismedia.Contracts.Entities;

/// <summary>API-facing consumption state shared by playable, readable, and viewable entities.</summary>
/// <param name="AccessCount">Number of explicit opens or starts.</param>
/// <param name="CompletionCount">Number of completed consumption events.</param>
/// <param name="SkipCount">Number of quick-abandon events.</param>
/// <param name="ActiveSeconds">Total actively reported consumption time.</param>
/// <param name="ResumeSeconds">Time-based resume position, when applicable.</param>
/// <param name="LastAccessedAt">Timestamp of the latest open/start event.</param>
/// <param name="LastActiveAt">Timestamp of the latest accepted progress or activity signal.</param>
/// <param name="CompletedAt">Timestamp when the entity most recently became complete.</param>
[CapabilityKind("consumption")]
public sealed record ConsumptionCapability(
    int AccessCount,
    int CompletionCount,
    int SkipCount,
    double ActiveSeconds,
    double ResumeSeconds,
    DateTimeOffset? LastAccessedAt,
    DateTimeOffset? LastActiveAt,
    DateTimeOffset? CompletedAt) : EntityCapability;
