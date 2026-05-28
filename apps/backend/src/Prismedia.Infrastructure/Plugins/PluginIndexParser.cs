using System.Text.Json;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

internal static class PluginIndexParser {
    public static IReadOnlyList<PluginIndexEntry> Parse(string body, string source) =>
        source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? ParseJson(body)
            : ParseYaml(body);

    private static IReadOnlyList<PluginIndexEntry> ParseJson(string json) {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var entries = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : root.TryGetProperty("plugins", out var plugins) && plugins.ValueKind == JsonValueKind.Array
                ? plugins.EnumerateArray()
                : [];

        return entries
            .Where(entry => entry.ValueKind == JsonValueKind.Object)
            .Select(ParseJsonEntry)
            .Where(IsUsable)
            .ToArray();
    }

    private static PluginIndexEntry ParseJsonEntry(JsonElement entry) =>
        new(
            Id: GetString(entry, "id"),
            Name: GetString(entry, "name"),
            Version: GetString(entry, "version"),
            Date: GetString(entry, "date"),
            Path: GetString(entry, "path", "downloadUrl"),
            Sha256: GetString(entry, "sha256"),
            Runtime: GetString(entry, "runtime", fallback: "dotnet-process"),
            IsNsfw: GetBool(entry, "isNsfw"),
            ManifestVersion: GetInt(entry, "manifestVersion", 1),
            ApiTags: GetStringArray(entry, "apiTags", ["prismedia"]),
            Compat: ParseJsonCompatibility(entry),
            Supports: ParseJsonSupports(entry));

    private static PluginCompatibility ParseJsonCompatibility(JsonElement entry) {
        if (!entry.TryGetProperty("compat", out var compat) || compat.ValueKind != JsonValueKind.Object) {
            return DefaultCompatibility();
        }

        return new PluginCompatibility(
            GetString(compat, "pluginApiMin", fallback: "1.0.0"),
            GetNullableString(compat, "pluginApiMax"),
            GetString(compat, "prismediaMin", fallback: "1.0.0"),
            GetNullableString(compat, "prismediaMax"));
    }

    private static IReadOnlyList<PluginEntitySupport> ParseJsonSupports(JsonElement entry) {
        if (!entry.TryGetProperty("supports", out var supports) || supports.ValueKind != JsonValueKind.Array) {
            return [];
        }

        return supports
            .EnumerateArray()
            .Where(support => support.ValueKind == JsonValueKind.Object)
            .Select(support => new PluginEntitySupport(
                GetString(support, "entityKind"),
                GetStringArray(support, "actions")))
            .Where(support => !string.IsNullOrWhiteSpace(support.EntityKind) && support.Actions.Count > 0)
            .ToArray();
    }

    private static IReadOnlyList<PluginIndexEntry> ParseYaml(string yaml) {
        var entries = new List<YamlEntry>();
        YamlEntry? entry = null;
        PluginEntitySupportBuilder? support = null;
        string? section = null;

        foreach (var rawLine in yaml.Split('\n')) {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) {
                continue;
            }

            var indent = line.Length - line.TrimStart(' ').Length;
            var trimmed = line.TrimStart();

            if (indent == 0 && trimmed.StartsWith("- ", StringComparison.Ordinal)) {
                if (entry is not null) {
                    entries.Add(entry);
                }

                entry = new YamlEntry();
                support = null;
                section = null;
                SetYamlScalar(entry, trimmed[2..]);
                continue;
            }

            if (entry is null) {
                continue;
            }

            if (indent == 2) {
                support = null;
                if (trimmed.EndsWith(':') && !trimmed.Contains(": ", StringComparison.Ordinal)) {
                    section = trimmed.TrimEnd(':');
                    continue;
                }

                section = null;
                SetYamlScalar(entry, trimmed);
                continue;
            }

            if (section == "apiTags" && indent == 4 && trimmed.StartsWith("- ", StringComparison.Ordinal)) {
                entry.ApiTags.Add(Unquote(trimmed[2..]));
                continue;
            }

            if (section == "compat" && indent == 4) {
                SetYamlCompatibility(entry, trimmed);
                continue;
            }

            if (section != "supports") {
                continue;
            }

            if (indent == 4 && trimmed.StartsWith("- ", StringComparison.Ordinal)) {
                support = new PluginEntitySupportBuilder();
                entry.Supports.Add(support);
                SetYamlSupportScalar(support, trimmed[2..]);
                continue;
            }

            if (support is null) {
                continue;
            }

            if (indent == 6 && trimmed.EndsWith(':') && trimmed.TrimEnd(':') == "actions") {
                continue;
            }

            if (indent == 8 && trimmed.StartsWith("- ", StringComparison.Ordinal)) {
                support.Actions.Add(Unquote(trimmed[2..]));
                continue;
            }

            if (indent == 6) {
                SetYamlSupportScalar(support, trimmed);
            }
        }

        if (entry is not null) {
            entries.Add(entry);
        }

        return entries
            .Select(entry => entry.ToIndexEntry())
            .Where(IsUsable)
            .ToArray();
    }

    private static void SetYamlScalar(YamlEntry entry, string line) {
        var (key, value) = SplitYamlPair(line);
        switch (key) {
            case "id":
                entry.Id = value;
                break;
            case "name":
                entry.Name = value;
                break;
            case "version":
                entry.Version = value;
                break;
            case "date":
                entry.Date = value;
                break;
            case "path":
            case "downloadUrl":
                entry.Path = value;
                break;
            case "sha256":
                entry.Sha256 = value;
                break;
            case "runtime":
                entry.Runtime = value;
                break;
            case "isNsfw":
                entry.IsNsfw = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            case "manifestVersion":
                entry.ManifestVersion = int.TryParse(value, out var manifestVersion) ? manifestVersion : 1;
                break;
        }
    }

    private static void SetYamlCompatibility(YamlEntry entry, string line) {
        var (key, value) = SplitYamlPair(line);
        switch (key) {
            case "pluginApiMin":
                entry.PluginApiMin = value;
                break;
            case "pluginApiMax":
                entry.PluginApiMax = NullIfYamlNull(value);
                break;
            case "prismediaMin":
                entry.PrismediaMin = value;
                break;
            case "prismediaMax":
                entry.PrismediaMax = NullIfYamlNull(value);
                break;
        }
    }

    private static void SetYamlSupportScalar(PluginEntitySupportBuilder support, string line) {
        var (key, value) = SplitYamlPair(line);
        if (key == "entityKind") {
            support.EntityKind = value;
        }
    }

    private static (string Key, string Value) SplitYamlPair(string line) {
        var separator = line.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0) {
            return (line.Trim(), string.Empty);
        }

        return (line[..separator].Trim(), Unquote(line[(separator + 1)..].Trim()));
    }

    private static string Unquote(string value) {
        if (value.Length >= 2 &&
            ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"'))) {
            return value[1..^1];
        }

        return value;
    }

    private static string? NullIfYamlNull(string value) =>
        value.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : value;

    private static bool IsUsable(PluginIndexEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Id) &&
        !string.IsNullOrWhiteSpace(entry.Name) &&
        !string.IsNullOrWhiteSpace(entry.Version) &&
        !string.IsNullOrWhiteSpace(entry.Path);

    private static PluginCompatibility DefaultCompatibility() =>
        new("1.0.0", null, "1.0.0", null);

    private static string GetString(JsonElement element, string name, string? alternate = null, string fallback = "") {
        if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String) {
            return property.GetString() ?? fallback;
        }

        if (alternate is not null &&
            element.TryGetProperty(alternate, out var alternateProperty) &&
            alternateProperty.ValueKind == JsonValueKind.String) {
            return alternateProperty.GetString() ?? fallback;
        }

        return fallback;
    }

    private static string? GetNullableString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static int GetInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : fallback;

    private static IReadOnlyList<string> GetStringArray(
        JsonElement element,
        string name,
        IReadOnlyList<string>? fallback = null) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray()
            : fallback ?? [];

    private sealed class YamlEntry {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string Runtime { get; set; } = "dotnet-process";
        public bool IsNsfw { get; set; }
        public int ManifestVersion { get; set; } = 1;
        public List<string> ApiTags { get; } = [];
        public string PluginApiMin { get; set; } = "1.0.0";
        public string? PluginApiMax { get; set; }
        public string PrismediaMin { get; set; } = "1.0.0";
        public string? PrismediaMax { get; set; }
        public List<PluginEntitySupportBuilder> Supports { get; } = [];

        public PluginIndexEntry ToIndexEntry() =>
            new(
                Id,
                Name,
                Version,
                Date,
                Path,
                Sha256,
                Runtime,
                IsNsfw,
                ManifestVersion,
                ApiTags.Count > 0 ? ApiTags : ["prismedia"],
                new PluginCompatibility(PluginApiMin, PluginApiMax, PrismediaMin, PrismediaMax),
                Supports
                    .Where(support => !string.IsNullOrWhiteSpace(support.EntityKind) && support.Actions.Count > 0)
                    .Select(support => new PluginEntitySupport(support.EntityKind, support.Actions))
                    .ToArray());
    }

    private sealed class PluginEntitySupportBuilder {
        public string EntityKind { get; set; } = string.Empty;
        public List<string> Actions { get; } = [];
    }
}
