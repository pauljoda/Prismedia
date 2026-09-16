using System.Text.Json;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using YamlDotNet.Serialization;

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
            .Select(TryParseJsonEntry)
            .OfType<PluginIndexEntry>()
            .Where(IsUsable)
            .ToArray();
    }

    private static PluginIndexEntry? TryParseJsonEntry(JsonElement entry) {
        try { return ParseJsonEntry(entry); }
        catch (JsonException) { return null; }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static PluginIndexEntry ParseJsonEntry(JsonElement entry) {
        var manifestVersion = GetInt(entry, "manifestVersion", 1);
        return new PluginIndexEntry(
            Id: GetString(entry, "id"),
            Name: GetString(entry, "name"),
            Version: GetString(entry, "version"),
            Date: GetString(entry, "date"),
            Path: GetString(entry, "path", "downloadUrl"),
            Sha256: GetString(entry, "sha256"),
            Runtime: GetString(entry, "runtime", fallback: DotnetPluginProcessRunner.Code),
            IsNsfw: GetBool(entry, "isNsfw"),
            ManifestVersion: manifestVersion,
            ApiTags: GetStringArray(entry, "apiTags", ["prismedia"]),
            Compat: ParseJsonCompatibility(entry),
            Supports: ParseJsonSupports(entry, manifestVersion),
            Execution: ParseJsonExecution(entry),
            Integration: entry.TryGetProperty("integration", out var integration) && integration.ValueKind != JsonValueKind.Null
                ? integration.Deserialize<PluginIntegrationDefinition>(PluginProcessTransport.JsonOptions)
                : null);
    }

    private static PluginExecutionPolicy? ParseJsonExecution(JsonElement entry) {
        if (!entry.TryGetProperty("execution", out var execution) || execution.ValueKind != JsonValueKind.Object) {
            return null;
        }

        return new PluginExecutionPolicy(
            GetInt(execution, "maxConcurrentInvocations", 0),
            GetInt(execution, "minimumStartIntervalMs", 0));
    }

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

    private static IReadOnlyList<PluginEntitySupport> ParseJsonSupports(JsonElement entry, int manifestVersion) {
        if (!entry.TryGetProperty("supports", out var supports) || supports.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var parsed = supports
            .EnumerateArray()
            .Select(support => support.ValueKind == JsonValueKind.Object
                ? new PluginEntitySupport(
                    GetString(support, "entityKind"),
                    manifestVersion == 2
                        ? GetDeclaredStringArray(support, "actions") ?? []
                        : GetStringArray(support, "actions"),
                    GetDeclaredStringArray(support, "identityNamespaces"),
                    ParseJsonSearch(support),
                    ParseJsonIdentityUrls(support))
                : new PluginEntitySupport(string.Empty, []))
            .ToArray();
        return manifestVersion == 2
            ? parsed
            : parsed.Where(support =>
                !string.IsNullOrWhiteSpace(support.EntityKind) && support.Actions.Count > 0).ToArray();
    }

    private static PluginSearchDefinition? ParseJsonSearch(JsonElement support) {
        if (!support.TryGetProperty("search", out var search) ||
            search.ValueKind != JsonValueKind.Object ||
            !search.TryGetProperty("fields", out var fields) ||
            fields.ValueKind != JsonValueKind.Array) {
            return null;
        }

        return new PluginSearchDefinition(fields
            .EnumerateArray()
            .Select(field => field.ValueKind == JsonValueKind.Object
                ? new PluginSearchField(
                    GetString(field, "key"),
                    GetString(field, "label"),
                    ParseSearchFieldType(GetString(field, "type")),
                    GetBool(field, "required"),
                    GetNullableString(field, "placeholder"),
                    GetNullableString(field, "help"))
                : new PluginSearchField(
                    string.Empty,
                    string.Empty,
                    (PluginSearchFieldType)(-1),
                    Required: false))
            .ToArray());
    }

    private static IReadOnlyList<PluginIdentityUrlFormat>? ParseJsonIdentityUrls(JsonElement support) {
        if (!support.TryGetProperty("identityUrls", out var formats)) {
            return null;
        }

        if (formats.ValueKind != JsonValueKind.Array) {
            return [new PluginIdentityUrlFormat(string.Empty, string.Empty, string.Empty)];
        }

        return formats
            .EnumerateArray()
            .Select(format => format.ValueKind == JsonValueKind.Object
                ? new PluginIdentityUrlFormat(
                    GetString(format, "identityNamespace"),
                    GetString(format, "valuePattern"),
                    GetString(format, "urlTemplate"))
                : new PluginIdentityUrlFormat(string.Empty, string.Empty, string.Empty))
            .ToArray();
    }

    private static IReadOnlyList<PluginIndexEntry> ParseYaml(string yaml) {
        // Use the same validation and nested contract parser for both distribution formats.
        var document = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization()
            .WithDuplicateKeyChecking().Build().Deserialize<object>(yaml);
        var json = new SerializerBuilder().JsonCompatible().Build().Serialize(document);
        return ParseJson(json);
    }

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

    private static IReadOnlyList<string>? GetDeclaredStringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? string.Empty
                    : string.Empty)
                .ToArray()
            : null;

    private static PluginSearchFieldType ParseSearchFieldType(string code) =>
        code.TryDecodeAs<PluginSearchFieldType>(out var type)
            ? type
            : (PluginSearchFieldType)(-1);

}
