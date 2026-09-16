namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>An immutable remote-to-local library boundary. Protection does not depend on connection health or scan enablement.</summary>
public sealed class ExternalLibraryMountRow {
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid LibraryRootId { get; set; }
    public string RemoteRootId { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
