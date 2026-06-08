using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

internal static class PluginEntityKindCompatibility {
    public static bool SupportsKind(PluginEntitySupport support, string requestedKind) =>
        support.EntityKind.Equals(requestedKind, StringComparison.OrdinalIgnoreCase) ||
        requestedKind.Equals(EntityKindRegistry.Movie.Code, StringComparison.OrdinalIgnoreCase) &&
        support.EntityKind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase);

    public static EntityKind RequestKindFor(PluginManifest manifest, string requestedKind) {
        if (manifest.Supports.Any(support => support.EntityKind.Equals(requestedKind, StringComparison.OrdinalIgnoreCase))) {
            return requestedKind.DecodeAs<EntityKind>();
        }

        return requestedKind.Equals(EntityKindRegistry.Movie.Code, StringComparison.OrdinalIgnoreCase) &&
            manifest.Supports.Any(support => support.EntityKind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase))
                ? EntityKind.Video
                : requestedKind.DecodeAs<EntityKind>();
    }

    public static IEnumerable<string> ActionsFor(PluginManifest manifest, string requestedKind) =>
        manifest.Supports
            .Where(support => SupportsKind(support, requestedKind))
            .SelectMany(support => support.Actions);
}
