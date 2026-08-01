using System.Reflection;

namespace Prismedia.Application.Settings;

/// <summary>Discovers the settings declared by <see cref="AppSettings"/>.</summary>
public static class AppSettingsRegistry {
    private static readonly IReadOnlyDictionary<string, SettingDefinition> ByKey;
    private static readonly IReadOnlyDictionary<string, SettingDefinition> ByClientName;

    static AppSettingsRegistry() {
        var discovered = typeof(AppSettings).GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(field => typeof(SettingDefinition).IsAssignableFrom(field.FieldType))
                .Select(field => (
                    ClientName: type.Name + field.Name,
                    Definition: (SettingDefinition?)field.GetValue(null))))
            .Select(item => item.Definition is null
                ? throw new InvalidOperationException($"Setting definition field '{item.ClientName}' is null.")
                : item)
            .OrderBy(item => item.Definition!.GroupOrder)
            .ThenBy(item => item.Definition!.Order)
            .ThenBy(item => item.Definition!.Key, StringComparer.Ordinal)
            .ToArray();
        Definitions = discovered.Select(item => item.Definition!).ToArray();
        ByKey = BuildLookup(discovered, item => item.Definition!.Key, "key");
        ByClientName = BuildLookup(discovered, item => item.ClientName, "client name");
    }

    /// <summary>All definitions in stable display order.</summary>
    public static IReadOnlyList<SettingDefinition> Definitions { get; }

    /// <summary>All definitions keyed by the stable generated-client symbol name.</summary>
    public static IReadOnlyDictionary<string, SettingDefinition> DefinitionsByClientName => ByClientName;

    /// <summary>Finds a definition by its stable dotted key.</summary>
    public static SettingDefinition? Find(string key) =>
        ByKey.TryGetValue(key, out var definition) ? definition : null;

    private static IReadOnlyDictionary<string, SettingDefinition> BuildLookup(
        IEnumerable<(string ClientName, SettingDefinition? Definition)> definitions,
        Func<(string ClientName, SettingDefinition? Definition), string> selector,
        string label) {
        var lookup = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions) {
            if (!lookup.TryAdd(selector(definition), definition.Definition!)) {
                throw new InvalidOperationException($"Duplicate setting {label} '{selector(definition)}'.");
            }
        }

        return lookup;
    }
}
