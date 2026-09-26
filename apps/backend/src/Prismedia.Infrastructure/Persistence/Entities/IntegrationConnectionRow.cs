using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Persistence record for one plugin-backed connection; credential values contain only authenticated ciphertext.</summary>
public sealed class IntegrationConnectionRow {
    public Guid Id { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public long Revision { get; set; }
    public ConnectionStatus Status { get; set; }
    public string EnabledCapabilitiesJson { get; set; } = "[]";
    public string SettingsJson { get; set; } = "{}";
    public string ProtectedSecretsJson { get; set; } = "{}";
    public string EffectiveCapabilitiesJson { get; set; } = "[]";
    public string? RemoteInstanceId { get; set; }
    public bool HasPersistentRemoteIdentity { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? LastError { get; set; }
}
