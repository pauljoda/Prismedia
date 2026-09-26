namespace Prismedia.Domain.Integrations;

/// <summary>An all-or-nothing decision. A review reason always accompanies an empty change list.</summary>
public sealed record ManagedSourceReconciliationPlan(IReadOnlyList<ManagedSourceChange> Changes, string? ReviewReason);
