using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

internal static class PluginEntityKindCompatibility {
    public static bool SupportsKind(PluginEntitySupport support, string requestedKind) =>
        support.EntityKind.Equals(requestedKind, StringComparison.OrdinalIgnoreCase) ||
        requestedKind.Equals(EntityKind.Movie.ToCode(), StringComparison.OrdinalIgnoreCase) &&
        support.EntityKind.Equals(EntityKind.Video.ToCode(), StringComparison.OrdinalIgnoreCase);

    public static EntityKind RequestKindFor(PluginManifest manifest, string requestedKind) {
        if (manifest.Supports.Any(support => support.EntityKind.Equals(requestedKind, StringComparison.OrdinalIgnoreCase))) {
            return requestedKind.DecodeAs<EntityKind>();
        }

        return requestedKind.Equals(EntityKind.Movie.ToCode(), StringComparison.OrdinalIgnoreCase) &&
            manifest.Supports.Any(support => support.EntityKind.Equals(EntityKind.Video.ToCode(), StringComparison.OrdinalIgnoreCase))
                ? EntityKind.Video
                : requestedKind.DecodeAs<EntityKind>();
    }

    public static IEnumerable<string> ActionsFor(PluginManifest manifest, string requestedKind) =>
        manifest.Supports
            .Where(support => SupportsKind(support, requestedKind))
            .SelectMany(support => support.Actions);
}
