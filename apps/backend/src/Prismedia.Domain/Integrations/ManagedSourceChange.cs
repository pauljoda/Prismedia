namespace Prismedia.Domain.Integrations;

/// <summary>
/// A source transition retaining its existing owners; null current evidence withdraws availability while keeping identities.
/// </summary>
public sealed record ManagedSourceChange(ManagedFileBinding Previous, ManagedObservedFile? Current);
