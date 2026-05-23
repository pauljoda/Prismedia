using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Selects plugin artifacts that are safe for the current Prismedia build.
/// </summary>
public static class PluginCompatibilityResolver {
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
            .Where(IsDotnetProcess)
            .Where(entry => SupportsAppVersion(entry.Compat, currentAppVersion))
            .OrderByDescending(entry => ParseVersion(entry.Version))
            .FirstOrDefault();
    }

    private static bool IsDotnetProcess(PluginIndexEntry entry) =>
        entry.ManifestVersion == 1 &&
        string.Equals(entry.Runtime, "dotnet-process", StringComparison.OrdinalIgnoreCase) &&
        entry.ApiTags.Any(tag => string.Equals(tag, "prismedia", StringComparison.OrdinalIgnoreCase));

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
}
