using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>Coordinates invocation admission for every runtime and connection using one plugin in a deployment.</summary>
public interface IPluginInvocationGate {
    /// <summary>Waits within the caller's deadline for shared capacity and start spacing. Disposal releases capacity, retaining the start clock.</summary>
    ValueTask<IAsyncDisposable> AcquireAsync(string pluginId, PluginExecutionPolicy policy, CancellationToken cancellationToken);
}
