using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Selects plugin artifacts that are safe for the current Prismedia build.
/// </summary>
public static class PluginCompatibilityResolver {
    private static readonly Version CurrentProtocolVersion = Version.Parse(PluginProtocol.CurrentSemanticVersion);

    /// <summary>
    /// Finds the newest dotnet-process artifact for a plugin that supports the current app version.
    /// </summary>
    /// <param name="entries">Community index entries to search.</param>
    /// <param name="pluginId">Plugin identifier requested by the app.</param>
    /// <param name="currentAppVersion">Current Prismedia version with any dev suffix already removed.</param>
    /// <returns>The latest compatible artifact, or null when none can run.</returns>
    public static PluginIndexEntry? LatestCompatible(
        IEnumerable<PluginIndexEntry> entries,
        string pluginId,
        Version currentAppVersion) {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(currentAppVersion);

        return entries
            .Where(entry => string.Equals(entry.Id, pluginId, StringComparison.OrdinalIgnoreCase))
            .Where(entry => IsCompatible(entry, currentAppVersion))
            .OrderByDescending(entry => ParseVersion(entry.Version))
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns whether a registry entry can run in the current app version.
    /// </summary>
    /// <param name="entry">Community index entry to inspect.</param>
    /// <param name="currentAppVersion">Current Prismedia version with any dev suffix already removed.</param>
    public static bool IsCompatible(PluginIndexEntry entry, Version currentAppVersion) =>
        IsDotnetProcess(entry) &&
        SupportsProtocolVersion(entry.Compat) &&
        SupportsAppVersion(entry.Compat, currentAppVersion);

    /// <summary>Returns whether a locally discovered manifest can run in the current app and plugin protocol.</summary>
    /// <param name="manifest">Installed plugin manifest to inspect.</param>
    /// <param name="currentAppVersion">Current Prismedia version with any dev suffix already removed.</param>
    public static bool IsCompatible(PluginManifest manifest, Version currentAppVersion) =>
        IsDotnetProcess(manifest) &&
        SupportsProtocolVersion(manifest.Compat) &&
        SupportsAppVersion(manifest.Compat, currentAppVersion);

    private static bool IsDotnetProcess(PluginIndexEntry entry) =>
        PluginManifestContract.IsValid(entry) &&
        string.Equals(entry.Runtime, "dotnet-process", StringComparison.OrdinalIgnoreCase) &&
        entry.ApiTags.Any(tag => string.Equals(tag, "prismedia", StringComparison.OrdinalIgnoreCase));

    private static bool IsDotnetProcess(PluginManifest manifest) =>
        PluginManifestContract.IsValid(manifest) &&
        string.Equals(manifest.Runtime, "dotnet-process", StringComparison.OrdinalIgnoreCase) &&
        manifest.ApiTags.Any(tag => string.Equals(tag, "prismedia", StringComparison.OrdinalIgnoreCase));

    private static bool SupportsProtocolVersion(PluginCompatibility compatibility) {
        if (!TryParseSemanticVersion(compatibility.PluginApiMin, out var min) ||
            CurrentProtocolVersion < min) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(compatibility.PluginApiMax)) {
            return true;
        }

        return TryParseSemanticVersion(compatibility.PluginApiMax, out var max) &&
            CurrentProtocolVersion <= max;
    }

    private static bool SupportsAppVersion(PluginCompatibility compatibility, Version current) {
        var min = ParseVersion(compatibility.PrismediaMin);
        if (current < min) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(compatibility.PrismediaMax)) {
            return true;
        }

        var max = ParseVersion(compatibility.PrismediaMax);
        return current <= max;
    }

    private static Version ParseVersion(string value) {
        var normalized = value.Split('-', 2)[0];
        return Version.TryParse(normalized, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static bool TryParseSemanticVersion(string? value, out Version version) {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var normalized = value.Split('-', 2)[0];
        if (!Version.TryParse(normalized, out var parsed) || parsed.Build < 0) {
            return false;
        }

        version = parsed;
        return true;
    }
}
