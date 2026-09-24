using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Discovers provider-owned libraries without coupling them to request-manager policy.</summary>
public sealed class ProviderLibraryService(
    IIntegrationConnectionStore connections,
    IntegrationConnectionAccess access,
    IIntegrationLibraryGateway gateway) {
    #region Static Variables

    private const int MaximumLibrariesPerConnection = 1000;

    #endregion

    #region Actions - Queries

    /// <summary>Lists provider libraries for every configured connected-library instance.</summary>
    public async Task<IReadOnlyList<ProviderLibraryConnection>> ListAsync(CancellationToken cancellationToken) {
        var stored = await connections.ListAsync(cancellationToken);
        var candidates = stored
            .Select(item => item.Connection.State)
            .Where(state => state.EnabledCapabilities.Contains(PluginCapability.ConnectedLibrary))
            .OrderBy(state => state.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.Id)
            .ToArray();
        var results = new List<ProviderLibraryConnection>(candidates.Length);
        foreach (var connection in candidates) {
            try {
                results.Add(await ListAsync(connection.Id, cancellationToken));
            } catch (Exception error) when (error is ConnectionNotFoundException or ConnectionCapabilityUnavailableException
                or ConnectionSecretUnavailableException or IntegrationInvocationException) {
                results.Add(new(connection.Id, connection.Name, connection.PluginId, [], error.Message));
            }
        }

        return results;
    }

    /// <summary>Lists and validates the complete provider library catalog for one connection.</summary>
    public async Task<ProviderLibraryConnection> ListAsync(Guid connectionId, CancellationToken cancellationToken) {
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ConnectedLibrary,
            IntegrationOperation.ListLibraries, cancellationToken);
        var catalog = await gateway.ListLibrariesAsync(authorized.Manifest.Id, authorized.Context, cancellationToken);
        var libraries = Validate(catalog, SupportedKinds(authorized));
        return new(connectionId, authorized.Connection.State.Name, authorized.Connection.State.PluginId, libraries);
    }

    private static IReadOnlySet<EntityKind> SupportedKinds(AuthorizedIntegrationConnection authorized) =>
        authorized.Connection.State.EffectiveCapabilities
            .Where(capability => capability.Kind == PluginCapability.ConnectedLibrary
                && capability.Operations.Contains(IntegrationOperation.ListLibraries))
            .SelectMany(capability => capability.EntityKinds)
            .Intersect(authorized.Manifest.Integration!.Capabilities
                .Where(capability => capability.Kind == PluginCapability.ConnectedLibrary
                    && capability.Operations.Contains(IntegrationOperation.ListLibraries))
                .SelectMany(capability => capability.EntityKinds))
            .ToHashSet();

    #endregion

    #region Actions - Validation

    private static IReadOnlyList<ProviderLibraryDescriptor> Validate(
        ProviderLibraryCatalog? catalog,
        IReadOnlySet<EntityKind> supportedKinds) {
        if (catalog?.Libraries is null || catalog.Libraries.Count > MaximumLibrariesPerConnection) {
            throw Invalid();
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in catalog.Libraries) {
            if (library is null || !Text(library.RemoteId, 512) || !Text(library.Label, 512)
                || !Text(library.RemotePath, 8192) || !ids.Add(library.RemoteId) || !paths.Add(library.RemotePath)
                || library.EntityKinds is not { Count: > 0 and <= 32 }
                || library.EntityKinds.Distinct().Count() != library.EntityKinds.Count
                || library.EntityKinds.Any(kind => !Enum.IsDefined(kind) || !supportedKinds.Contains(kind))
                || !ManagementUrl(library.ManagementUrl)) {
                throw Invalid();
            }
        }

        return catalog.Libraries.ToArray();
    }

    private static bool ManagementUrl(string? value) {
        if (value is null) {
            return true;
        }

        return value.Length <= 8192 && !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0;
    }

    private static bool Text(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private static IntegrationInvocationException Invalid() =>
        new("The connected application returned an invalid or oversized library catalog.");

    #endregion
}
