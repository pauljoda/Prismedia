using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Stable content identity within one connected holding, with coordinates that must not silently change.</summary>
public sealed record ManagedTargetIdentity(string RemoteTargetId, EntityKind Kind, int? SeasonNumber, int? EpisodeNumber,
    int? AbsoluteNumber, string? IssueLabel = null);
