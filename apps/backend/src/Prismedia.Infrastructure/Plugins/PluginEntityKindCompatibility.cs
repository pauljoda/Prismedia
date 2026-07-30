using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

internal static class PluginEntityKindCompatibility {
    public static bool SupportsKind(PluginEntitySupport support, string requestedKind) =>
        support.EntityKind.Equals(requestedKind, StringComparison.OrdinalIgnoreCase) ||
        TryGetFallbackKind(requestedKind, out var fallbackKind) &&
        support.EntityKind.Equals(fallbackKind.ToCode(), StringComparison.OrdinalIgnoreCase);

    public static EntityKind RequestKindFor(PluginManifest manifest, string requestedKind) {
        if (manifest.Supports.Any(support => support.EntityKind.Equals(requestedKind, StringComparison.OrdinalIgnoreCase))) {
            return requestedKind.DecodeAs<EntityKind>();
        }

        return TryGetFallbackKind(requestedKind, out var fallbackKind) &&
            manifest.Supports.Any(support => support.EntityKind.Equals(fallbackKind.ToCode(), StringComparison.OrdinalIgnoreCase))
                ? fallbackKind
                : requestedKind.DecodeAs<EntityKind>();
    }

    public static IEnumerable<string> ActionsFor(PluginManifest manifest, string requestedKind) =>
        manifest.Supports
            .Where(support => SupportsKind(support, requestedKind))
            .SelectMany(support => support.Actions);

    private static bool TryGetFallbackKind(string requestedKind, out EntityKind fallbackKind) {
        if (requestedKind.Equals(requestedKind.Trim(), StringComparison.Ordinal) &&
            EntityKindRegistry.TryDescribe(requestedKind, out var definition) &&
            definition.Identification.PluginFallbackKind is { } fallback) {
            fallbackKind = fallback;
            return true;
        }

        fallbackKind = default;
        return false;
    }
}
