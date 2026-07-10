using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Resolves provider links from normalized installed-plugin support declarations.
/// </summary>
public sealed class PluginIdentityUrlResolver(IPluginCatalogService catalog) : IPluginIdentityUrlResolver {
    /// <inheritdoc />
    public async Task<string?> ResolveAsync(
        string entityKindCode,
        PluginIdentityRoute route,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(entityKindCode) ||
            string.IsNullOrWhiteSpace(route.PluginId)) {
            return null;
        }

        var providers = await catalog.ListInstalledProvidersAsync(cancellationToken);
        var provider = providers.FirstOrDefault(candidate =>
            candidate.Installed &&
            candidate.Enabled &&
            string.Equals(candidate.Id, route.PluginId, StringComparison.OrdinalIgnoreCase));
        if (provider is null) {
            return null;
        }

        var exactSupports = provider.Supports
            .Where(support =>
                support.EntityKind.Equals(entityKindCode, StringComparison.OrdinalIgnoreCase) &&
                (support.IdentityNamespaces ?? []).Contains(route.Identity.Namespace, StringComparer.Ordinal))
            .ToArray();
        var matchingSupports = exactSupports.Length > 0
            ? exactSupports
            : provider.Supports
                .Where(support =>
                    PluginEntityKindCompatibility.SupportsKind(support, entityKindCode) &&
                    (support.IdentityNamespaces ?? []).Contains(route.Identity.Namespace, StringComparer.Ordinal))
                .ToArray();
        var matchingFormats = matchingSupports
            .SelectMany(support => support.IdentityUrls ?? [])
            .Where(format => string.Equals(
                format.IdentityNamespace,
                route.Identity.Namespace,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matchingFormats.Length != 1) {
            return null;
        }

        return PluginIdentityUrlFormatContract.TryBuild(
            matchingFormats[0],
            route.Identity.Value,
            out var url)
                ? url
                : null;
    }
}
