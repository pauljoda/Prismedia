namespace Prismedia.Application.Integrations;

/// <summary>A materialized wanted work and the explicit targets that still need fulfillment.</summary>
/// <param name="EntityId">The wanted work's local identity.</param>
/// <param name="MissingTargetEntityIds">Explicit targets without local files, or null when the work is requested as a whole.</param>
/// <param name="HasEveryFile">Whether nothing is left for a manager to deliver.</param>
public sealed record ManagedWantedWork(Guid EntityId, IReadOnlyList<Guid>? MissingTargetEntityIds, bool HasEveryFile);
