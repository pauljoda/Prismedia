using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Persisted configuration and health of one independently configured remote application.</summary>
public sealed record IntegrationConnectionState(
    Guid Id, string PluginId, string Name, string BaseUrl, bool Enabled,
    IReadOnlyList<PluginCapability> EnabledCapabilities, IReadOnlyDictionary<string, string> Settings,
    long Revision, ConnectionStatus Status, string? RemoteInstanceId,
    IReadOnlyList<IntegrationSupport> EffectiveCapabilities, DateTimeOffset? LastCheckedAt, string? LastError,
    bool HasPersistentRemoteIdentity = false);
