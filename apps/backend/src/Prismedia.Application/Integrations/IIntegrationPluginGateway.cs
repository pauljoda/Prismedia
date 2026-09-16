using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Integrations;

/// <summary>Installed integration plugin lookup and typed remote capability probing.</summary>
public interface IIntegrationPluginGateway {
    /// <summary>Returns an installed and enabled plugin, or null if no usable integration artifact exists.</summary>
    Task<PluginManifest?> FindAsync(string pluginId, CancellationToken cancellationToken);
    /// <summary>Performs a bounded read-only probe; reports failures without including credentials.</summary>
    Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken cancellationToken);
}

/// <summary>A trusted plugin failed its operation or returned an invalid response.</summary>
public sealed class IntegrationInvocationException(string message) : Exception(message);
