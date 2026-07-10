namespace Prismedia.Application.Plugins;

/// <summary>
/// Resolves an authoritative plugin identity route to the provider's canonical web page.
/// </summary>
public interface IPluginIdentityUrlResolver {
    /// <summary>
    /// Builds a safe provider URL from the exact enabled plugin, entity kind, and identity route.
    /// </summary>
    /// <param name="entityKindCode">Canonical Prismedia entity kind code.</param>
    /// <param name="route">Authoritative plugin and persistent external identity association.</param>
    /// <param name="cancellationToken">Cancellation token for the catalog lookup.</param>
    /// <returns>A safe absolute HTTP(S) URL, or <see langword="null"/> when no exact format applies.</returns>
    Task<string?> ResolveAsync(
        string entityKindCode,
        PluginIdentityRoute route,
        CancellationToken cancellationToken);
}
