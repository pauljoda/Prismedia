using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Local source ownership and numbering observed independently of the connected application.</summary>
public sealed record ManagedLocalSource(Guid EntityId, Guid SourceFileId, string LocalPath, EntityKind Kind,
    int? SeasonNumber, int? EpisodeNumber, int? AbsoluteNumber, string? IssueLabel = null, Guid? ParentEntityId = null);
