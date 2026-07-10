using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Validates the manifest-v2 capability contract and expands legacy manifest-v1 declarations at
/// the catalog boundary. Runtime and API consumers therefore receive one complete support shape.
/// </summary>
internal static class PluginManifestContract {
    private static readonly string SearchAction = IdentifyAction.Search.ToCode();

    /// <summary>Returns whether the manifest schema and support declarations are usable.</summary>
    internal static bool IsValid(PluginManifest manifest) =>
        IsValid(manifest.ManifestVersion, manifest.Id, manifest.Supports);

    /// <summary>Returns whether the index entry schema and support declarations are usable.</summary>
    internal static bool IsValid(PluginIndexEntry entry) =>
        IsValid(entry.ManifestVersion, entry.Id, entry.Supports);

    /// <summary>Returns a manifest whose support declarations are complete for runtime consumers.</summary>
    internal static PluginManifest Normalize(PluginManifest manifest) =>
        manifest with {
            Supports = NormalizeSupports(manifest.ManifestVersion, manifest.Id, manifest.Supports)
        };

    /// <summary>Returns an index entry whose support declarations are complete for API consumers.</summary>
    internal static PluginIndexEntry Normalize(PluginIndexEntry entry) =>
        entry with {
            Supports = NormalizeSupports(entry.ManifestVersion, entry.Id, entry.Supports)
        };

    private static bool IsValid(
        int manifestVersion,
        string pluginId,
        IReadOnlyList<PluginEntitySupport>? supports) {
        if (manifestVersion is not (1 or 2)) {
            return false;
        }

        // Manifest v1 did not declare identity namespaces or search forms. Its plugin id is the
        // compatibility identity namespace, so it must at least be convertible into that shape.
        if (manifestVersion == 1) {
            return TryNormalizeIdentityNamespace(pluginId, out _);
        }

        if (supports is not { Count: > 0 }) {
            return false;
        }

        var kinds = new HashSet<string>(StringComparer.Ordinal);
        return supports.All(support =>
            support is not null &&
            IsValidVersionTwoSupport(support) &&
            kinds.Add(support.EntityKind));
    }

    private static bool IsValidVersionTwoSupport(PluginEntitySupport support) {
        if (!EntityKindRegistry.TryGet(support.EntityKind, out var entityKind) ||
            !string.Equals(support.EntityKind, entityKind.ToCode(), StringComparison.Ordinal) ||
            support.Actions is not { Count: > 0 } ||
            support.IdentityNamespaces is not { Count: > 0 }) {
            return false;
        }

        var actions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in support.Actions) {
            if (string.IsNullOrWhiteSpace(action) ||
                !action.TryDecodeAs<IdentifyAction>(out var identifyAction) ||
                !string.Equals(action, identifyAction.ToCode(), StringComparison.Ordinal) ||
                !actions.Add(action)) {
                return false;
            }
        }

        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identityNamespace in support.IdentityNamespaces) {
            if (!TryNormalizeIdentityNamespace(identityNamespace, out var normalized) ||
                !string.Equals(identityNamespace, normalized, StringComparison.Ordinal) ||
                !namespaces.Add(normalized)) {
                return false;
            }
        }

        var identityUrlNamespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identityUrl in support.IdentityUrls ?? []) {
            if (!PluginIdentityUrlFormatContract.IsValid(identityUrl, namespaces) ||
                !identityUrlNamespaces.Add(identityUrl.IdentityNamespace)) {
                return false;
            }
        }

        var declaresSearch = actions.Contains(SearchAction);
        if (declaresSearch != (support.Search is not null)) {
            return false;
        }

        return support.Search is null || IsUsableSearch(support.Search);
    }

    private static bool IsUsableSearch(PluginSearchDefinition? search) {
        if (search?.Fields is not { Count: > 0 }) {
            return false;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in search.Fields) {
            if (field is null ||
                !IsStableFieldKey(field.Key) ||
                string.IsNullOrWhiteSpace(field.Label) ||
                !string.Equals(field.Key, field.Key.Trim(), StringComparison.Ordinal) ||
                !string.Equals(field.Label, field.Label.Trim(), StringComparison.Ordinal) ||
                !Enum.IsDefined(field.Type) ||
                !keys.Add(field.Key)) {
                return false;
            }
        }

        return true;
    }

    private static bool IsStableFieldKey(string? key) {
        if (string.IsNullOrWhiteSpace(key) || !IsAsciiLetter(key[0])) {
            return false;
        }

        return key.All(character =>
            IsAsciiLetter(character) ||
            character is >= '0' and <= '9' or '-' or '_' or '.');
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static IReadOnlyList<PluginEntitySupport> NormalizeSupports(
        int manifestVersion,
        string pluginId,
        IReadOnlyList<PluginEntitySupport>? supports) {
        if (supports is null || supports.Count == 0) {
            return [];
        }

        var legacyNamespace = string.Empty;
        if (manifestVersion == 1 && !TryNormalizeIdentityNamespace(pluginId, out legacyNamespace)) {
            // Compatibility validation rejects this case before normalization. Keep this helper
            // total so malformed data can never turn a catalog read into an exception.
            return [];
        }

        return supports.Select(support => {
            var actions = support.Actions ?? [];
            var namespaces = manifestVersion == 1
                ? NormalizeLegacyNamespaces(support.IdentityNamespaces, legacyNamespace)
                : support.IdentityNamespaces!.ToArray();
            var declaresSearch = actions.Any(action =>
                string.Equals(action, SearchAction, StringComparison.OrdinalIgnoreCase));
            var search = declaresSearch
                ? IsUsableSearch(support.Search) ? support.Search : DefaultTitleSearch()
                : support.Search is not null && IsUsableSearch(support.Search) ? support.Search : null;
            var identityUrls = manifestVersion == 1
                ? []
                : (support.IdentityUrls ?? []).ToArray();

            return support with {
                Actions = actions.ToArray(),
                IdentityNamespaces = namespaces,
                Search = search,
                IdentityUrls = identityUrls
            };
        }).ToArray();
    }

    private static IReadOnlyList<string> NormalizeLegacyNamespaces(
        IReadOnlyList<string>? declared,
        string fallback) {
        if (declared is not { Count: > 0 }) {
            return [fallback];
        }

        var normalized = declared
            .Select(identityNamespace =>
                TryNormalizeIdentityNamespace(identityNamespace, out var value) ? value : null)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return normalized.Length > 0 ? normalized : [fallback];
    }

    private static PluginSearchDefinition DefaultTitleSearch() =>
        new(
        [
            new PluginSearchField(
                "title",
                "Title",
                PluginSearchFieldType.Text,
                Required: true,
                Placeholder: "Search title")
        ]);

    private static bool TryNormalizeIdentityNamespace(string? value, out string normalized) {
        try {
            normalized = new ExternalIdentity(value ?? string.Empty, "manifest-validation").Namespace;
            return true;
        } catch (ArgumentException) {
            normalized = string.Empty;
            return false;
        }
    }
}
