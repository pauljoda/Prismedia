namespace Prismedia.Domain.Entities;

/// <summary>The local kind and remote shape of the targets a connected holding contains.</summary>
/// <param name="Kind">Local Entity kind each target binds to.</param>
/// <param name="Shape">How the manager identifies each target.</param>
public sealed record ManagedTarget(EntityKind Kind, ManagedTargetShape Shape);
