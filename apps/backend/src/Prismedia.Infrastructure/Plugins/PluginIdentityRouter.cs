using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Routes persistent identities through normalized installed-plugin support declarations.
/// </summary>
public sealed class PluginIdentityRouter(IPluginCatalogService catalog) : IPluginIdentityRouter {
    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
        string entityKindCode,
        IdentifyAction action,
        IReadOnlyList<ExternalIdentity> identities,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(entityKindCode) || identities.Count == 0) {
            return [];
        }

        var actionCode = action.ToCode();
        var providers = (await catalog.ListInstalledProvidersAsync(cancellationToken))
            .Where(provider => provider.Enabled)
            .OrderBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(provider => provider.Id, StringComparer.Ordinal)
            .ToArray();
        var routes = new HashSet<PluginIdentityRoute>(PluginIdentityRouteComparer.Instance);

        foreach (var identity in identities
            .Distinct()
            .OrderBy(identity => identity.Namespace, StringComparer.Ordinal)
            .ThenBy(identity => identity.Value, StringComparer.Ordinal)) {
            foreach (var provider in providers) {
                var supported = provider.Supports.Any(support =>
                    PluginEntityKindCompatibility.SupportsKind(support, entityKindCode)
                    && support.Actions.Contains(actionCode, StringComparer.OrdinalIgnoreCase)
                    && (support.IdentityNamespaces ?? []).Contains(identity.Namespace, StringComparer.Ordinal));
                if (supported) {
                    routes.Add(new PluginIdentityRoute(provider.Id, identity));
                }
            }
        }

        return routes
            .OrderBy(route => route.Identity.Namespace, StringComparer.Ordinal)
            .ThenBy(route => route.Identity.Value, StringComparer.Ordinal)
            .ThenBy(route => route.PluginId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(route => route.PluginId, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class PluginIdentityRouteComparer : IEqualityComparer<PluginIdentityRoute> {
        public static PluginIdentityRouteComparer Instance { get; } = new();

        public bool Equals(PluginIdentityRoute? left, PluginIdentityRoute? right) =>
            ReferenceEquals(left, right)
            || left is not null && right is not null
            && left.Identity == right.Identity
            && string.Equals(left.PluginId, right.PluginId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(PluginIdentityRoute route) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(route.PluginId), route.Identity);
    }
}
