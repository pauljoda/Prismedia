using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Resolves the <see cref="IIdentifyRunner"/> that owns a provider artifact's runtime.
/// </summary>
public sealed class IdentifyRunnerSelector {
    private readonly IReadOnlyDictionary<string, IIdentifyRunner> _runnersByRuntime;

    /// <summary>
    /// Creates a selector over the registered identify runners.
    /// </summary>
    /// <param name="runners">All identify runners discovered through dependency injection.</param>
    public IdentifyRunnerSelector(IEnumerable<IIdentifyRunner> runners) {
        ArgumentNullException.ThrowIfNull(runners);

        var runnersByRuntime = new Dictionary<string, IIdentifyRunner>(StringComparer.OrdinalIgnoreCase);
        foreach (var runner in runners) {
            ArgumentNullException.ThrowIfNull(runner);
            if (string.IsNullOrWhiteSpace(runner.RuntimeCode)) {
                throw new InvalidOperationException(
                    $"Identify runner '{runner.GetType().Name}' does not declare a runtime code.");
            }

            if (!runnersByRuntime.TryAdd(runner.RuntimeCode, runner)) {
                throw new InvalidOperationException(
                    $"Multiple identify runners claim runtime '{runner.RuntimeCode}'.");
            }
        }

        _runnersByRuntime = runnersByRuntime;
    }

    /// <summary>
    /// Returns the runner that owns the descriptor's runtime.
    /// </summary>
    /// <param name="descriptor">Resolved provider artifact to execute.</param>
    /// <returns>The runner that owns the descriptor's runtime.</returns>
    /// <exception cref="InvalidOperationException">No runner supports the descriptor's runtime.</exception>
    public IIdentifyRunner Resolve(PluginDescriptor descriptor) {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!_runnersByRuntime.TryGetValue(descriptor.Manifest.Runtime, out var runner)) {
            throw new InvalidOperationException(
                $"No identify runner supports runtime '{descriptor.Manifest.Runtime}'.");
        }

        return new IdentitySafeIdentifyRunner(runner);
    }

    private sealed class IdentitySafeIdentifyRunner(IIdentifyRunner inner) : IIdentifyRunner {
        public string RuntimeCode => inner.RuntimeCode;

        public async Task<IdentifyPluginResponse> IdentifyAsync(
            PluginDescriptor descriptor,
            IdentifyPluginRequest request,
            CancellationToken cancellationToken) {
            var response = await inner.IdentifyAsync(descriptor, request, cancellationToken);
            return response.Result is null
                ? response
                : response with {
                    Result = EntityMetadataProposalIdentityPolicy.RemoveSharedStructuralIdentities(response.Result)
                };
        }
    }
}
