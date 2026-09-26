namespace Prismedia.Domain.Integrations;

/// <summary>One remote target's established local identity and source-file evidence.</summary>
public sealed record ManagedEntityBinding(ManagedTargetIdentity Target, Guid EntityId, Guid SourceFileId);
