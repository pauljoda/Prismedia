using Prismedia.Application.Settings;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Chooses the metadata providers automatic identification tries for one Entity kind. A non-empty
/// "Enabled plugins" list is used in its configured order; an empty list means each kind uses its
/// default metadata provider, the same provider Identify opens with.
/// </summary>
internal static class AutoIdentifyProviderSelection {
    /// <summary>
    /// Returns the provider ids to try for an Entity kind.
    /// </summary>
    /// <param name="configuredProviders">The saved auto-identify plugin list, in priority order.</param>
    /// <param name="compatibleProviders">
    /// Providers compatible with <paramref name="entityKind"/>, ordered by
    /// <see cref="IdentifyProviderDefaultPolicy"/> so a usable configured default comes first.
    /// </param>
    /// <param name="entityKind">Concrete Entity kind code being identified.</param>
    /// <param name="identifySettings">Per-kind default metadata providers.</param>
    /// <param name="isSelectable">Caller's eligibility rule for an explicitly listed provider.</param>
    /// <returns>Provider ids in the order they should be tried; empty when none can run.</returns>
    internal static IReadOnlyList<string> Select(
        IReadOnlyList<string> configuredProviders,
        IReadOnlyList<PluginProvider> compatibleProviders,
        string entityKind,
        IdentifyProviderSettings identifySettings,
        Func<PluginProvider, bool> isSelectable) {
        if (configuredProviders.Count > 0) {
            var selectable = compatibleProviders
                .Where(isSelectable)
                .Select(provider => provider.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return configuredProviders
                .Where(selectable.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return DefaultProviderId(compatibleProviders, entityKind, identifySettings) is { } defaultId
            ? [defaultId]
            : [];
    }

    /// <summary>
    /// Returns the kind's default provider: the configured default when it is usable, otherwise the
    /// first usable compatible provider. An implicit choice never selects an NSFW provider; only an
    /// administrator's explicit per-kind default can.
    /// </summary>
    private static string? DefaultProviderId(
        IReadOnlyList<PluginProvider> compatibleProviders,
        string entityKind,
        IdentifyProviderSettings identifySettings) {
        identifySettings.DefaultProviders.TryGetValue(entityKind, out var configuredId);
        return compatibleProviders
            .FirstOrDefault(provider =>
                provider is { Installed: true, Enabled: true, MissingAuthKeys.Count: 0 } &&
                (!provider.IsNsfw || provider.Id.Equals(configuredId, StringComparison.OrdinalIgnoreCase)))
            ?.Id;
    }
}
