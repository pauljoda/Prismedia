using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>One server-derived local wanted target paired with the manager lookup evidence sent for it.</summary>
public sealed record ManagedRequestEntityTarget(Guid EntityId, ManagedLookupTarget Target);
