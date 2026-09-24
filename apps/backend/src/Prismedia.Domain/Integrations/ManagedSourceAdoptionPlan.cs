namespace Prismedia.Domain.Integrations;

/// <summary>Exact source bindings, or one reason that the complete selection needs review.</summary>
public sealed record ManagedSourceAdoptionPlan(IReadOnlyList<ManagedFileBinding> Bindings, string? ReviewReason);
