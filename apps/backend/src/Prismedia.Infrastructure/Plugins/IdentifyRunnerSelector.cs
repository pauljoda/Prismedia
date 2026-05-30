namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Resolves the <see cref="IIdentifyRunner"/> that owns a provider artifact's runtime.
/// </summary>
public sealed class IdentifyRunnerSelector {
    private readonly IReadOnlyList<IIdentifyRunner> _runners;

    /// <summary>
    /// Creates a selector over the registered identify runners.
    /// </summary>
    /// <param name="runners">All identify runners discovered through dependency injection.</param>
    public IdentifyRunnerSelector(IEnumerable<IIdentifyRunner> runners) {
        _runners = runners.ToArray();
    }

    /// <summary>
    /// Returns the first runner that can execute the descriptor.
    /// </summary>
    /// <param name="descriptor">Resolved provider artifact to execute.</param>
    /// <returns>The runner that owns the descriptor's runtime.</returns>
    /// <exception cref="InvalidOperationException">No runner supports the descriptor's runtime.</exception>
    public IIdentifyRunner Resolve(PluginDescriptor descriptor) =>
        _runners.FirstOrDefault(runner => runner.CanRun(descriptor)) ??
        throw new InvalidOperationException(
            $"No identify runner supports runtime '{descriptor.Manifest.Runtime}'.");
}
