namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Persisted authoritative plugin route for one Entity identity.</summary>
public sealed class EntityProviderIdentityRow {
    public Guid EntityId { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string IdentityNamespace { get; set; } = string.Empty;
    public string IdentityValue { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
