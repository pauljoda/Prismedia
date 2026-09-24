using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Rechecks current package authority, connection health, and credentials for every non-probe invocation.</summary>
public sealed class IntegrationConnectionAccess(IIntegrationConnectionStore store, IIntegrationPluginGateway plugins) {
    #region Actions - Authorization

    /// <summary>Loads credentials after verifying an operation that applies to the connection's declared kind set.</summary>
    public async Task<AuthorizedIntegrationConnection> RequireAsync(Guid id, PluginCapability capability,
        IntegrationOperation operation, CancellationToken cancellationToken) {
        var connection = (await store.FindAsync(id, cancellationToken))?.Connection ?? throw new ConnectionNotFoundException();
        var manifest = await plugins.FindAsync(connection.State.PluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        var negotiated = connection.State.Enabled && connection.State.Status == ConnectionStatus.Ready
            && connection.State.EffectiveCapabilities.Any(support => support.Kind == capability && support.Operations.Contains(operation));
        var packaged = manifest.Integration?.Capabilities.Any(support => support.Kind == capability
            && support.Operations.Contains(operation) && support.EntityKinds.Count > 0) == true;
        if (!negotiated || !packaged || !PluginCapabilityDefinition.For(capability).Allows(operation)) {
            throw new ConnectionCapabilityUnavailableException();
        }

        var auth = IntegrationCredentialScope.ForManifest(manifest,
            await store.ReadSecretsAsync(id, manifest.Auth.Select(field => field.Key).ToArray(), cancellationToken));
        return new(connection, manifest, new(id, connection.State.BaseUrl,
            connection.State.HasPersistentRemoteIdentity ? connection.State.RemoteInstanceId : null, connection.State.Settings, auth));
    }

    /// <summary>Loads only this connection's credentials after verifying both negotiated and current installed support.</summary>
    public async Task<AuthorizedIntegrationConnection> RequireAsync(Guid id, PluginCapability capability,
        IntegrationOperation operation, EntityKind kind, CancellationToken cancellationToken) {
        var connection = (await store.FindAsync(id, cancellationToken))?.Connection ?? throw new ConnectionNotFoundException();
        var manifest = await plugins.FindAsync(connection.State.PluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        if (!connection.Allows(capability, operation, kind) || !PluginCapabilityDefinition.For(capability).Allows(operation)
            || manifest.Integration?.Capabilities.Any(support => support.Kind == capability
                && support.Operations.Contains(operation) && support.EntityKinds.Contains(kind)) != true) {
            throw new ConnectionCapabilityUnavailableException();
        }

        var auth = IntegrationCredentialScope.ForManifest(manifest,
            await store.ReadSecretsAsync(id, manifest.Auth.Select(field => field.Key).ToArray(), cancellationToken));
        return new(connection, manifest, new(id, connection.State.BaseUrl,
            connection.State.HasPersistentRemoteIdentity ? connection.State.RemoteInstanceId : null, connection.State.Settings, auth));
    }

    #endregion
}
