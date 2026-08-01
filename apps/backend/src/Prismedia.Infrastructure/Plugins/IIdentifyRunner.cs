using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Executes a single identify request for a resolved provider artifact.
/// Implementations are selected by the runtime declared in the provider manifest,
/// allowing the identify orchestration to stay agnostic of how a provider runs
/// (dotnet child process, Stash-compatible scraper engine, and so on).
/// </summary>
public interface IIdentifyRunner {
    /// <summary>
    /// Gets the manifest runtime code exclusively owned by this runner.
    /// </summary>
    string RuntimeCode { get; }

    /// <summary>
    /// Runs one identify request and returns the provider's proposal or candidate response.
    /// </summary>
    /// <param name="descriptor">Resolved provider artifact to execute.</param>
    /// <param name="request">Identify request envelope describing the entity, action, and hints.</param>
    /// <param name="cancellationToken">Token used to cancel execution.</param>
    /// <returns>The provider response, including success state and any error message.</returns>
    Task<IdentifyPluginResponse> IdentifyAsync(
        PluginDescriptor descriptor,
        IdentifyPluginRequest request,
        CancellationToken cancellationToken);
}
