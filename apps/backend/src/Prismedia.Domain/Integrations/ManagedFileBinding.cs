namespace Prismedia.Domain.Integrations;

/// <summary>Established ownership of one physical file, including every episode sharing its bytes.</summary>
public sealed record ManagedFileBinding(string RemoteFileId, string LocalPath, long SizeBytes, DateTimeOffset WrittenAt,
    bool IsAvailable, IReadOnlyList<ManagedEntityBinding> Entities);
