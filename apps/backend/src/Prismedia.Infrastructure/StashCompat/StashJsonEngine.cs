using System.Text.Json;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Evaluates a Stash <c>jsonScrapers</c> scene block against a fetched JSON document.
/// Implements the common GJSON path subset (dotted keys, numeric array indexes, and the
/// <c>#</c> wildcard that flattens array element values); unsupported path syntax fails soft
/// by yielding no value rather than throwing.
/// </summary>
public sealed class StashJsonEngine {
    /// <summary>
    /// Evaluates the <c>scene</c> definition of a JSON scraper block against a JSON body.
    /// </summary>
    /// <param name="json">Raw JSON document text.</param>
    /// <param name="scraperBlock">The resolved <c>jsonScrapers</c> entry.</param>
    /// <returns>The scraped scene, or null when nothing usable was extracted.</returns>
    public StashScrapedScene? EvaluateScene(string json, StashYamlNode scraperBlock) {
        var sceneDef = scraperBlock["scene"];
        if (sceneDef.IsMissing) {
            return null;
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(json);
        } catch (JsonException) {
            return null;
        }

        using (document) {
            var common = scraperBlock["common"];
            var scene = new StashScrapedScene();

            foreach (var (field, selectorDef) in sceneDef.Entries()) {
                switch (field.ToLowerInvariant()) {
                    case "title":
                        scene.Title = EvaluateString(document.RootElement, selectorDef, common);
                        break;
                    case "date":
                        scene.Date = EvaluateString(document.RootElement, selectorDef, common);
                        break;
                    case "details":
                        scene.Details = EvaluateString(document.RootElement, selectorDef, common);
                        break;
                    case "url":
                        scene.Url = EvaluateString(document.RootElement, selectorDef, common);
                        break;
                    case "image":
                        scene.Image = EvaluateString(document.RootElement, selectorDef, common);
                        break;
                    case "code":
                        scene.Code = EvaluateString(document.RootElement, selectorDef, common);
                        break;
                    case "tags":
                        scene.Tags = EvaluateArray(document.RootElement, selectorDef, common);
                        break;
                    case "performers":
                        scene.Performers = EvaluateArray(document.RootElement, selectorDef, common);
                        break;
                    case "studio":
                        var name = EvaluateString(document.RootElement, selectorDef["Name"], common);
                        if (!string.IsNullOrWhiteSpace(name)) {
                            scene.Studio = new StashScrapedStudio { Name = name };
                        }

                        break;
                }
            }

            return scene.HasData ? scene : null;
        }
    }

    private static string? EvaluateString(JsonElement root, StashYamlNode fieldDef, StashYamlNode common) {
        var (path, postProcess) = ParseFieldDef(fieldDef, common);
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        var value = SelectFirst(root, path);
        if (string.IsNullOrEmpty(value)) {
            return null;
        }

        var processed = StashSelector.ApplyPostProcess(value.Trim(), postProcess);
        return string.IsNullOrWhiteSpace(processed) ? null : processed;
    }

    private static IReadOnlyList<string> EvaluateArray(JsonElement root, StashYamlNode subObject, StashYamlNode common) {
        var nameField = subObject.HasKey("Name") ? subObject["Name"] : subObject["name"];
        if (nameField.IsMissing) {
            return [];
        }

        var (path, postProcess) = ParseFieldDef(nameField, common);
        if (string.IsNullOrWhiteSpace(path)) {
            return [];
        }

        var results = new List<string>();
        foreach (var value in SelectAll(root, path)) {
            var processed = StashSelector.ApplyPostProcess(value.Trim(), postProcess);
            if (!string.IsNullOrWhiteSpace(processed)) {
                results.Add(processed);
            }
        }

        return results;
    }

    private static (string Path, StashYamlNode PostProcess) ParseFieldDef(StashYamlNode fieldDef, StashYamlNode common) {
        if (fieldDef.Scalar is { } scalar) {
            return (StashSelector.ApplyCommon(scalar, common), StashYamlNode.Parse(string.Empty));
        }

        var selector = fieldDef.StringAt("selector") ?? string.Empty;
        return (StashSelector.ApplyCommon(selector, common), fieldDef["postProcess"]);
    }

    private static string? SelectFirst(JsonElement root, string path) =>
        SelectAll(root, path).FirstOrDefault();

    private static IEnumerable<string> SelectAll(JsonElement root, string path) {
        var current = new List<JsonElement> { root };

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) {
            var next = new List<JsonElement>();
            foreach (var element in current) {
                Descend(element, segment, next);
            }

            current = next;
            if (current.Count == 0) {
                yield break;
            }
        }

        foreach (var element in current) {
            if (Scalar(element) is { } value) {
                yield return value;
            }
        }
    }

    private static void Descend(JsonElement element, string segment, List<JsonElement> sink) {
        if (segment == "#") {
            if (element.ValueKind == JsonValueKind.Array) {
                sink.AddRange(element.EnumerateArray());
            }

            return;
        }

        if (int.TryParse(segment, out var index)) {
            if (element.ValueKind == JsonValueKind.Array && index >= 0 && index < element.GetArrayLength()) {
                sink.Add(element[index]);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment, out var property)) {
            sink.Add(property);
        }
    }

    private static string? Scalar(JsonElement element) =>
        element.ValueKind switch {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
}
