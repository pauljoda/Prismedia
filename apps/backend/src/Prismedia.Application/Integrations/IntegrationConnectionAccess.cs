using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Authorized connection context for a specific declared operation, populated only immediately before invocation.</summary>
public sealed record AuthorizedIntegrationConnection(IntegrationConnection Connection, PluginManifest Manifest, IntegrationConnectionContext Context);

/// <summary>Rechecks current package authority, connection health, and credentials for every non-probe invocation.</summary>
public sealed class IntegrationConnectionAccess(IIntegrationConnectionStore store, IIntegrationPluginGateway plugins) {
    /// <summary>Loads only this connection's credentials after verifying both negotiated and current installed support.</summary>
    public async Task<AuthorizedIntegrationConnection> RequireAsync(Guid id, PluginCapability capability,
        IntegrationOperation operation, EntityKind kind, CancellationToken cancellationToken) {
        var connection = (await store.FindAsync(id, cancellationToken))?.Connection ?? throw new ConnectionNotFoundException();
        var manifest = await plugins.FindAsync(connection.State.PluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        if (!connection.Allows(capability, operation, kind) || !PluginCapabilityPolicy.Allows(capability, operation)
            || manifest.Integration?.Capabilities.Any(support => support.Kind == capability
                && support.Operations.Contains(operation) && support.EntityKinds.Contains(kind)) != true)
            throw new ConnectionCapabilityUnavailableException();
        var auth = IntegrationCredentialScope.ForManifest(manifest,
            await store.ReadSecretsAsync(id, manifest.Auth.Select(field => field.Key).ToArray(), cancellationToken));
        return new(connection, manifest, new(id, connection.State.BaseUrl,
            connection.State.HasPersistentRemoteIdentity ? connection.State.RemoteInstanceId : null, connection.State.Settings, auth));
    }
}

/// <summary>Current connection configuration has not authorized this operation; retained intent may be retried after configuration recovers.</summary>
public sealed class ConnectionCapabilityUnavailableException() : ArgumentException(
    "This connection does not currently support the requested operation. Check its enabled capabilities and test it again.");
