using Prismedia.Application.Settings;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Orders compatible Identify providers around an administrator's per-kind default while preserving
/// catalog order whenever the configured id is missing or cannot currently be used.
/// </summary>
internal static class IdentifyProviderDefaultPolicy {
    /// <summary>
    /// Places the usable configured default first for a known requested EntityKind.
    /// Unknown kinds and stale provider ids leave the original catalog order unchanged.
    /// </summary>
    internal static IReadOnlyList<PluginProvider> Order(
        IEnumerable<PluginProvider> providers,
        string? requestedEntityKind,
        IdentifyProviderSettings settings) {
        var ordered = providers.ToArray();
        if (string.IsNullOrWhiteSpace(requestedEntityKind) ||
            !EntityKindRegistry.TryGet(requestedEntityKind, out _) ||
            !settings.DefaultProviders.TryGetValue(requestedEntityKind, out var configuredProviderId)) {
            return ordered;
        }

        var configuredIndex = Array.FindIndex(ordered, provider =>
            provider.Id.Equals(configuredProviderId, StringComparison.OrdinalIgnoreCase) &&
            IsUsableForKind(provider, requestedEntityKind));
        if (configuredIndex <= 0) {
            return ordered;
        }

        return [
            ordered[configuredIndex],
            .. ordered.Take(configuredIndex),
            .. ordered.Skip(configuredIndex + 1),
        ];
    }

    private static bool IsUsableForKind(PluginProvider provider, string entityKind) =>
        provider.Installed &&
        provider.Enabled &&
        provider.MissingAuthKeys.Count == 0 &&
        provider.Supports.Any(support => PluginEntityKindCompatibility.SupportsKind(support, entityKind));
}
