namespace Prismedia.Domain.Integrations;

/// <summary>An administrator's explicit association between a remote target and its already scanned local source.</summary>
public sealed record ManagedBindingSelection(string RemoteTargetId, Guid EntityId, Guid SourceFileId);
