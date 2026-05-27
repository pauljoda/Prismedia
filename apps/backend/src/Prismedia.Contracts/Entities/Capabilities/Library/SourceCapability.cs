namespace Prismedia.Contracts.Entities;

/// <summary>API-facing source provenance path or library reference.</summary>
/// <param name="Code">Stable source code, such as library-root, folder, relative, file, archive, or zip.</param>
/// <param name="Value">Path, identifier, or source value.</param>
public sealed record EntitySource(string Code, string Value);

/// <summary>API-facing source provenance capability.</summary>
[CapabilityKind("source")]
public sealed record SourceCapability(IReadOnlyList<EntitySource> Items) : EntityCapability;
