using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>An immutable mapping from a connected application's root to a dedicated, read-only local library.</summary>
public sealed record ExternalLibraryMount(Guid Id, Guid ConnectionId, Guid LibraryRootId, string RemoteRootId,
    string RemotePath, string LocalPath, string Label);

/// <summary>Creates a mapping after checking the remote root still has the path the user reviewed. Scanning starts disabled.</summary>
public sealed record CreateExternalLibraryMountRequest(EntityKind EntityKind, string RemoteRootId,
    string ExpectedRemotePath, string LocalPath, string Label, bool IsNsfw = false);

/// <summary>
/// Attaches a reviewed external root to an existing local library without changing that library's
/// path, settings, access grants, entities, or files.
/// </summary>
public sealed record AttachExistingExternalLibraryMountRequest(EntityKind EntityKind, string RemoteRootId,
    string ExpectedRemotePath, Guid ExistingLibraryRootId, string ExpectedLocalPath);

/// <summary>Local filesystem evidence for one remote file. Matching size establishes readable bytes, not an imported entity or verified content hash.</summary>
public sealed record MappedLibraryFile(string RemoteId, Guid? LibraryRootId, string? LocalPath,
    bool IsReadable, bool SizeMatches, string? Problem);

/// <summary>Fresh remote holding evidence and independently checked local file access.</summary>
public sealed record MappedLibrarySnapshot(ManagedItemSnapshot Remote, IReadOnlyList<MappedLibraryFile> Files);
