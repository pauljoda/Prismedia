using System.Globalization;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Coordinates validated connection configuration, protected credentials, and remote capability negotiation.</summary>
public sealed class ConnectionService(IIntegrationConnectionStore store, IIntegrationPluginGateway plugins) {
    /// <summary>Lists connections without exposing secret values.</summary>
    public async Task<IReadOnlyList<ConnectionResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).Select(ToResponse).ToArray();

    /// <summary>Loads the public configuration of one connection.</summary>
    public async Task<ConnectionResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await RequireAsync(id, cancellationToken));

    /// <summary>Creates another independent instance of an installed integration plugin.</summary>
    public async Task<ConnectionResponse> CreateAsync(CreateConnectionRequest request, CancellationToken cancellationToken) {
        var manifest = await RequirePluginAsync(request.PluginId, cancellationToken);
        Validate(manifest, request.EnabledCapabilities, request.Settings, request.Secrets);
        var connection = IntegrationConnection.Create(manifest.Id, request.Name, request.BaseUrl, request.Enabled,
            request.EnabledCapabilities, request.Settings);
        await store.SaveAsync(connection, null, request.Secrets, cancellationToken);
        return await GetAsync(connection.State.Id, cancellationToken);
    }

    /// <summary>Replaces settings, applies explicit secret edits, and requires fresh negotiation.</summary>
    public async Task<ConnectionResponse> UpdateAsync(Guid id, UpdateConnectionRequest request, CancellationToken cancellationToken) {
        var connection = (await RequireAsync(id, cancellationToken)).Connection;
        if (connection.State.Revision != request.ExpectedRevision) throw new ConnectionConflictException();
        var manifest = await RequirePluginAsync(connection.State.PluginId, cancellationToken);
        Validate(manifest, request.EnabledCapabilities, request.Settings, request.Secrets);
        connection.Configure(request.Name, request.BaseUrl, request.Enabled, request.EnabledCapabilities, request.Settings);
        await store.SaveAsync(connection, request.ExpectedRevision, request.Secrets, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Tests the configured application with its own credentials and saves the intersection of permitted support.</summary>
    public async Task<ConnectionResponse> ProbeAsync(Guid id, CancellationToken cancellationToken) {
        var connection = (await RequireAsync(id, cancellationToken)).Connection;
        var revision = connection.State.Revision;
        if (!connection.State.Enabled) throw new ArgumentException("Enable the connection before testing it.");
        try {
            var manifest = await RequirePluginAsync(connection.State.PluginId, cancellationToken);
            var auth = IntegrationCredentialScope.ForManifest(manifest,
                await store.ReadSecretsAsync(id, manifest.Auth.Select(field => field.Key).ToArray(), cancellationToken));
            var result = await plugins.ProbeAsync(manifest.Id, new IntegrationConnectionContext(id, connection.State.BaseUrl,
                connection.State.HasPersistentRemoteIdentity ? connection.State.RemoteInstanceId : null, connection.State.Settings, auth), cancellationToken);
            if (result.Capabilities is null || result.Capabilities.Count > 8 || result.Capabilities.Any(item => item is null)
                || result.InstanceId?.Length > 512 || result.InstanceId is not null && string.IsNullOrWhiteSpace(result.InstanceId))
                throw new IntegrationInvocationException("The application returned an invalid capability probe.");
            var effective = IntegrationCapabilityNegotiation.Intersect(manifest.Integration!.Capabilities,
                result.Capabilities, connection.State.EnabledCapabilities);
            connection.RecordProbe(result.InstanceId ?? id.ToString("N"), effective.Select(item =>
                new IntegrationSupport(item.Kind, item.Operations, item.EntityKinds)).ToArray(), null,
                DateTimeOffset.UtcNow, result.InstanceId is not null);
        } catch (IntegrationInvocationException error) {
            connection.RecordProbe(null, [], error.Message, DateTimeOffset.UtcNow);
        } catch (ConnectionSecretUnavailableException error) {
            connection.RecordProbe(null, [], error.Message, DateTimeOffset.UtcNow);
        }
        // The revision check also prevents a slow probe from overwriting settings saved while it ran.
        await store.SaveAsync(connection, revision, new Dictionary<string, string?>(), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes a connection only when the stored revision still matches.</summary>
    public Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, expectedRevision, cancellationToken);

    private async Task<StoredIntegrationConnection> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) ?? throw new ConnectionNotFoundException();

    private async Task<PluginManifest> RequirePluginAsync(string pluginId, CancellationToken cancellationToken) =>
        await plugins.FindAsync(pluginId, cancellationToken) ?? throw new IntegrationInvocationException("Install and enable a compatible integration plugin first.");

    private static void Validate(PluginManifest manifest, IReadOnlyList<PluginCapability> capabilities,
        IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string?> secrets) {
        if (manifest.Integration is null || capabilities is null || capabilities.Any(kind => !manifest.Integration.Capabilities.Any(item => item.Kind == kind)))
            throw new ArgumentException("The plugin does not declare the selected capabilities.");
        if (settings is null || secrets is null || secrets.Count > 32
            || secrets.Any(pair => pair.Value?.Length > 8192 || !manifest.Auth.Any(field => field.Key == pair.Key)))
            throw new ArgumentException("Use only credentials declared by this plugin, within the size limits.");
        var fields = manifest.Integration.Settings;
        if (settings.Any(pair => !fields.Any(field => field.Key == pair.Key))) throw new ArgumentException("The plugin does not declare one of these settings.");
        foreach (var field in fields) {
            settings.TryGetValue(field.Key, out var value);
            if (string.IsNullOrWhiteSpace(value)) {
                if (field.Required) throw new ArgumentException($"{field.Label} is required.");
                continue;
            }
            if (field.Type == PluginSearchFieldType.Number && !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                || field.Type == PluginSearchFieldType.Year && (!int.TryParse(value, out var year) || year is < 1000 or > 9999))
                throw new ArgumentException($"{field.Label} must contain a valid number.");
        }
    }

    private static ConnectionResponse ToResponse(StoredIntegrationConnection stored) {
        var value = stored.Connection.State;
        return new(value.Id, value.PluginId, value.Name, value.BaseUrl, value.Enabled, value.EnabledCapabilities,
            value.Settings, stored.ConfiguredSecretKeys, value.Revision, value.Status, value.RemoteInstanceId,
            value.HasPersistentRemoteIdentity, value.EffectiveCapabilities.Select(item =>
                new PluginIntegrationCapability(item.Kind, item.Operations, item.EntityKinds)).ToArray(), value.LastCheckedAt, value.LastError);
    }
}
