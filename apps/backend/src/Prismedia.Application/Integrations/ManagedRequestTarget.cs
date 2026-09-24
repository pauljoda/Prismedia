using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Server-derived wanted identity and immutable mapped boundary.</summary>
public sealed record ManagedRequestTarget(Guid EntityId, string Title, ManagedLookupInput Work,
    ExternalLibraryMount Mount, IReadOnlyList<ManagedRequestEntityTarget>? Targets = null);
