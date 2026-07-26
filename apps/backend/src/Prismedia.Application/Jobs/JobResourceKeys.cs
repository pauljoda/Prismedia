namespace Prismedia.Application.Jobs;

/// <summary>Canonical dynamic key construction for shared job scheduler resources.</summary>
public static class JobResourceKeys {
    public const string EntityPrefix = "entity:";
    public const string PluginPrefix = "plugin:";

    public static string Entity(string entityId) => $"{EntityPrefix}{entityId}";

    public static string Plugin(string pluginId) => $"{PluginPrefix}{pluginId}";

    public static bool IsEntity(string key) => key.StartsWith(EntityPrefix, StringComparison.Ordinal);
}
