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
    /// Indicates whether this runner can execute the supplied provider artifact.
    /// </summary>
    /// <param name="descriptor">Resolved provider artifact, including its manifest runtime.</param>
    /// <returns>True when this runner owns the descriptor's runtime.</returns>
    bool CanRun(PluginDescriptor descriptor);

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
