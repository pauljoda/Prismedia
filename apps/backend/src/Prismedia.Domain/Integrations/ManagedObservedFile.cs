namespace Prismedia.Domain.Integrations;

/// <summary>
/// Fresh mapped file evidence. Readable means local bytes can be opened and match the reported size, not that a provider
/// supplied a content hash.
/// </summary>
public sealed record ManagedObservedFile(string RemoteFileId, string LocalPath, long SizeBytes, DateTimeOffset WrittenAt,
    bool IsReadable, IReadOnlyList<ManagedTargetIdentity> Targets);
