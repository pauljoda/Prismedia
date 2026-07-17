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
                        scene.Tags = EvaluateTagArray(document.RootElement, selectorDef, common);
                        break;
                    case "performers": // prism-vocab: external — Stash scraper mapping key.
                        scene.Performers = EvaluateArray(document.RootElement, selectorDef, common)
                            .Select(name => new StashScrapedPerformer { Name = name })
                            .ToArray();
                        break;
                    case "studio":
                        scene.Studio = EvaluateStudioObject(document.RootElement, selectorDef, common);
                        break;
                }
            }

            return scene.HasData ? scene : null;
        }
    }

    private static string? EvaluateString(JsonElement root, StashYamlNode fieldDef, StashYamlNode common) {
        if (FixedValue(fieldDef) is { } fixedValue) {
            return fixedValue;
        }

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

    private static IReadOnlyList<string> EvaluateArray(JsonElement root, StashYamlNode subObject, StashYamlNode common) =>
        EvaluateFieldArray(root, subObject, common, "Name", "name");

    private static IReadOnlyList<string> EvaluateFieldArray(
        JsonElement root,
        StashYamlNode subObject,
        StashYamlNode common,
        params string[] fieldNames) {
        var field = FieldByName(subObject, fieldNames);
        if (field.IsMissing) {
            return [];
        }

        if (FixedValue(field) is { } fixedValue) {
            return [fixedValue];
        }

        var (path, postProcess) = ParseFieldDef(field, common);
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

    /// <summary>
    /// Evaluates the <c>tag</c> definition of a JSON scraper block against a JSON body.
    /// </summary>
    /// <param name="json">Raw JSON document text.</param>
    /// <param name="scraperBlock">The resolved <c>jsonScrapers</c> entry.</param>
    /// <returns>The scraped tag, or null when nothing usable was extracted.</returns>
    public StashScrapedTag? EvaluateTag(string json, StashYamlNode scraperBlock) {
        var tagDef = scraperBlock["tag"];
        if (tagDef.IsMissing) {
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
            var tag = new StashScrapedTag {
                Name = EvaluateNamedField(document.RootElement, tagDef, common, "Name", "name"),
                Url = EvaluateNamedField(document.RootElement, tagDef, common, "URL", "URLs", "url", "urls"),
                Image = EvaluateNamedField(document.RootElement, tagDef, common, "Image", "image"),
                Description = EvaluateNamedField(document.RootElement, tagDef, common, "Details", "Description", "details", "description"),
                Aliases = EvaluateNamedField(document.RootElement, tagDef, common, "Aliases", "aliases")
            };
            return tag.HasData ? tag : null;
        }
    }

    /// <summary>
    /// Evaluates a tag search-results block by zipping repeated Name/URL/Image fields into candidates.
    /// </summary>
    public IReadOnlyList<StashScrapedTag> EvaluateTagList(string json, StashYamlNode scraperBlock) {
        var tagDef = scraperBlock["tag"];
        if (tagDef.IsMissing) {
            return [];
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(json);
        } catch (JsonException) {
            return [];
        }

        using (document) {
            var common = scraperBlock["common"];
            var names = EvaluateFieldArray(document.RootElement, tagDef, common, "Name", "name");
            var urls = EvaluateFieldArray(document.RootElement, tagDef, common, "URL", "URLs", "url", "urls");
            var images = EvaluateFieldArray(document.RootElement, tagDef, common, "Image", "image");
            var descriptions = EvaluateFieldArray(document.RootElement, tagDef, common, "Details", "Description", "details", "description");
            var aliases = EvaluateFieldArray(document.RootElement, tagDef, common, "Aliases", "aliases");
            var count = new[] { names.Count, urls.Count, images.Count, descriptions.Count, aliases.Count }.Max();
            var tags = new List<StashScrapedTag>(count);
            for (var index = 0; index < count; index++) {
                var tag = new StashScrapedTag {
                    Name = At(names, index),
                    Url = At(urls, index),
                    Image = At(images, index),
                    Description = At(descriptions, index),
                    Aliases = At(aliases, index)
                };
                if (tag.HasData) {
                    tags.Add(tag);
                }
            }

            return tags;
        }
    }

    /// <summary>
    /// Evaluates the <c>studio</c> definition of a JSON scraper block against a JSON body.
    /// </summary>
    /// <param name="json">Raw JSON document text.</param>
    /// <param name="scraperBlock">The resolved <c>jsonScrapers</c> entry.</param>
    /// <returns>The scraped studio, or null when nothing usable was extracted.</returns>
    public StashScrapedStudio? EvaluateStudio(string json, StashYamlNode scraperBlock) {
        var studioDef = scraperBlock["studio"];
        if (studioDef.IsMissing) {
            return null;
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(json);
        } catch (JsonException) {
            return null;
        }

        using (document) {
            return EvaluateStudioObject(document.RootElement, studioDef, scraperBlock["common"]);
        }
    }

    /// <summary>
    /// Evaluates a studio search-results block by zipping repeated Name/URL/Image fields into candidates.
    /// </summary>
    public IReadOnlyList<StashScrapedStudio> EvaluateStudioList(string json, StashYamlNode scraperBlock) {
        var studioDef = scraperBlock["studio"];
        if (studioDef.IsMissing) {
            return [];
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(json);
        } catch (JsonException) {
            return [];
        }

        using (document) {
            var common = scraperBlock["common"];
            var names = EvaluateFieldArray(document.RootElement, studioDef, common, "Name", "name");
            var urls = EvaluateFieldArray(document.RootElement, studioDef, common, "URL", "URLs", "url", "urls");
            var images = EvaluateFieldArray(document.RootElement, studioDef, common, "Image", "image");
            var descriptions = EvaluateFieldArray(document.RootElement, studioDef, common, "Details", "Description", "details", "description");
            var count = new[] { names.Count, urls.Count, images.Count, descriptions.Count }.Max();
            var studios = new List<StashScrapedStudio>(count);
            for (var index = 0; index < count; index++) {
                var studio = new StashScrapedStudio {
                    Name = At(names, index),
                    Url = At(urls, index),
                    Image = At(images, index),
                    Description = At(descriptions, index)
                };
                if (studio.HasData) {
                    studios.Add(studio);
                }
            }

            return studios;
        }
    }

    private static IReadOnlyList<StashScrapedTag> EvaluateTagArray(JsonElement root, StashYamlNode subObject, StashYamlNode common) {
        var names = EvaluateArray(root, subObject, common);
        var urls = EvaluateFieldArray(root, subObject, common, "URL", "URLs", "url", "urls");
        var images = EvaluateFieldArray(root, subObject, common, "Image", "image");
        var descriptions = EvaluateFieldArray(root, subObject, common, "Details", "Description", "details", "description");
        var aliases = EvaluateFieldArray(root, subObject, common, "Aliases", "aliases");
        var tags = new List<StashScrapedTag>(names.Count);
        for (var index = 0; index < names.Count; index++) {
            tags.Add(new StashScrapedTag {
                Name = names[index],
                Url = index < urls.Count ? urls[index] : null,
                Image = index < images.Count ? images[index] : null,
                Description = index < descriptions.Count ? descriptions[index] : null,
                Aliases = index < aliases.Count ? aliases[index] : null
            });
        }

        return tags;
    }

    private static StashScrapedStudio? EvaluateStudioObject(JsonElement root, StashYamlNode studioDef, StashYamlNode common) {
        var studio = new StashScrapedStudio {
            Name = EvaluateNamedField(root, studioDef, common, "Name", "name"),
            Url = EvaluateNamedField(root, studioDef, common, "URL", "URLs", "url", "urls"),
            Image = EvaluateNamedField(root, studioDef, common, "Image", "image"),
            Description = EvaluateNamedField(root, studioDef, common, "Details", "Description", "details", "description")
        };
        return studio.HasData ? studio : null;
    }

    private static string? EvaluateNamedField(JsonElement root, StashYamlNode subObject, StashYamlNode common, params string[] fieldNames) {
        var field = FieldByName(subObject, fieldNames);
        return field.IsMissing ? null : EvaluateString(root, field, common);
    }

    private static StashYamlNode FieldByName(StashYamlNode subObject, params string[] names) {
        foreach (var name in names) {
            if (subObject.HasKey(name)) {
                return subObject[name];
            }
        }

        return StashYamlNode.Parse(string.Empty);
    }

    private static string? At(IReadOnlyList<string> values, int index) =>
        index < values.Count ? values[index] : null;

    private static string? FixedValue(StashYamlNode fieldDef) {
        var fixedValue = fieldDef.StringAt("fixed");
        if (string.IsNullOrWhiteSpace(fixedValue)) {
            return null;
        }

        var processed = StashSelector.ApplyPostProcess(fixedValue, fieldDef["postProcess"]);
        return string.IsNullOrWhiteSpace(processed) ? null : processed;
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
