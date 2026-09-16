using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Usable operations and media kinds in a single responsibility family.</summary>
public sealed record IntegrationSupport(PluginCapability Kind, IReadOnlyList<IntegrationOperation> Operations, IReadOnlyList<EntityKind> EntityKinds);

/// <summary>Persisted configuration and health of one independently configured remote application.</summary>
public sealed record IntegrationConnectionState(
    Guid Id, string PluginId, string Name, string BaseUrl, bool Enabled,
    IReadOnlyList<PluginCapability> EnabledCapabilities, IReadOnlyDictionary<string, string> Settings,
    long Revision, ConnectionStatus Status, string? RemoteInstanceId,
    IReadOnlyList<IntegrationSupport> EffectiveCapabilities, DateTimeOffset? LastCheckedAt, string? LastError, bool HasPersistentRemoteIdentity = false);

/// <summary>Owns connection identity and prevents stale configuration or a replaced remote app from receiving authority.</summary>
public sealed class IntegrationConnection(IntegrationConnectionState state) {
    /// <summary>Immutable current state for persistence and projections.</summary>
    public IntegrationConnectionState State { get; private set; } = state;

    /// <summary>Creates a new unverified connection with an independent local identity.</summary>
    public static IntegrationConnection Create(string pluginId, string name, string baseUrl, bool enabled,
        IReadOnlyList<PluginCapability> capabilities, IReadOnlyDictionary<string, string> settings) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var connection = new IntegrationConnection(new(Guid.NewGuid(), pluginId, name, baseUrl, enabled,
            capabilities, settings, 0, ConnectionStatus.Unverified, null, [], null, null));
        connection.Configure(name, baseUrl, enabled, capabilities, settings);
        return connection;
    }

    /// <summary>Changes configuration and revokes negotiated support until the new configuration is verified.</summary>
    public void Configure(string name, string baseUrl, bool enabled, IReadOnlyList<PluginCapability> capabilities,
        IReadOnlyDictionary<string, string> settings) {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw new ArgumentException("A connection name of at most 200 characters is required.");
        if (baseUrl is null || baseUrl.Length > 2048 || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new ArgumentException("Use an absolute HTTP or HTTPS base URL without embedded credentials, query, or fragment.");
        if (capabilities is not { Count: > 0 and <= 8 } || capabilities.Distinct().Count() != capabilities.Count
            || capabilities.Any(capability => !Enum.IsDefined(capability) || capability == PluginCapability.Metadata))
            throw new ArgumentException("Select at least one supported connection capability.");
        if (settings is null || settings.Count > 32 || settings.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
            || pair.Key.Length > 100 || pair.Value is null || pair.Value.Length > 4096)) throw new ArgumentException("Connection settings exceed their limits.");
        // A bound endpoint is immutable: changing its location could send existing job IDs to another server.
        // Create a new connection for a moved/replaced server; explicit rebinding requires fulfillment reconciliation.
        if (State.RemoteInstanceId is not null && !string.Equals(new Uri(State.BaseUrl).AbsoluteUri, endpoint.AbsoluteUri, StringComparison.Ordinal))
            throw new ArgumentException("Create a new connection to change the address of a verified remote application.");
        State = State with {
            Name = name.Trim(), BaseUrl = endpoint.AbsoluteUri, Enabled = enabled,
            EnabledCapabilities = capabilities.ToArray(), Settings = new Dictionary<string, string>(settings),
            Revision = State.Revision + 1, Status = enabled ? ConnectionStatus.Unverified : ConnectionStatus.Disabled,
            EffectiveCapabilities = [], LastCheckedAt = null, LastError = null
        };
    }

    /// <summary>Records a probe while retaining established identity across outages and remote replacement.</summary>
    public void RecordProbe(string? remoteInstanceId, IReadOnlyList<IntegrationSupport> effective, string? error, DateTimeOffset now, bool hasPersistentRemoteIdentity = true) {
        var changedIdentity = State.RemoteInstanceId is not null && !string.Equals(State.RemoteInstanceId, remoteInstanceId, StringComparison.Ordinal);
        var status = !State.Enabled ? ConnectionStatus.Disabled
            : error is not null ? ConnectionStatus.Unavailable
            : changedIdentity ? ConnectionStatus.IdentityChanged
            : effective.Count == 0 ? ConnectionStatus.Unavailable : ConnectionStatus.Ready;
        State = State with {
            Revision = State.Revision + 1, Status = status, LastCheckedAt = now,
            RemoteInstanceId = State.RemoteInstanceId ?? (error is null ? remoteInstanceId : null),
            HasPersistentRemoteIdentity = State.RemoteInstanceId is not null ? State.HasPersistentRemoteIdentity : error is null && hasPersistentRemoteIdentity,
            EffectiveCapabilities = status == ConnectionStatus.Ready ? effective.ToArray() : [],
            LastError = error ?? (changedIdentity ? "The remote installation identity changed. Create a new connection before using this application."
                : effective.Count == 0 ? "No enabled capabilities are available from this application." : null)
        };
    }

    /// <summary>Whether this verified connection may execute the requested operation for an entity kind.</summary>
    public bool Allows(PluginCapability capability, IntegrationOperation operation, EntityKind kind) =>
        State.Enabled && State.Status == ConnectionStatus.Ready && State.EffectiveCapabilities.Any(support =>
            support.Kind == capability && support.Operations.Contains(operation) && support.EntityKinds.Contains(kind));
}
