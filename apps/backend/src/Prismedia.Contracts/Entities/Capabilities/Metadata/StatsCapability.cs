namespace Prismedia.Contracts.Entities;

/// <summary>API-facing stored or derived statistic.</summary>
/// <param name="Code">Stable statistic code, such as images, tracks, pages, chapters, or items.</param>
/// <param name="Value">Non-negative statistic value.</param>
public sealed record EntityStat(string Code, int Value);

/// <summary>API-facing stored or derived statistic capability.</summary>
[CapabilityKind("stats")]
public sealed record StatsCapability(IReadOnlyList<EntityStat> Items) : EntityCapability;
