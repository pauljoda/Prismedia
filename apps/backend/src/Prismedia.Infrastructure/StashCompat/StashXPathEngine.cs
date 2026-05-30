using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.XPath;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Evaluates a Stash <c>xPathScrapers</c> scene/performer block against parsed HTML.
/// Uses AngleSharp's spec-compliant DOM plus AngleSharp.XPath for selector evaluation,
/// matching the behavior the reference engine got from a browser DOM (including attribute
/// node selection such as <c>@href</c> and multi-node array fields driven by a Name selector).
/// </summary>
public sealed class StashXPathEngine {
    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// Evaluates the <c>scene</c> definition of an XPath scraper block against page HTML.
    /// </summary>
    /// <param name="html">Raw page HTML.</param>
    /// <param name="scraperBlock">The resolved <c>xPathScrapers</c> entry (with <c>common</c> and <c>scene</c>).</param>
    /// <returns>The scraped scene, or null when no usable field was extracted.</returns>
    public StashScrapedScene? EvaluateScene(string html, StashYamlNode scraperBlock) {
        var sceneDef = scraperBlock["scene"];
        if (sceneDef.IsMissing) {
            return null;
        }

        var document = Parser.ParseDocument(html);
        var common = scraperBlock["common"];
        var scene = new StashScrapedScene();

        foreach (var (field, selectorDef) in sceneDef.Entries()) {
            switch (field.ToLowerInvariant()) {
                case "title":
                    scene.Title = EvaluateString(document, selectorDef, common);
                    break;
                case "date":
                    scene.Date = EvaluateString(document, selectorDef, common);
                    break;
                case "details":
                    scene.Details = EvaluateString(document, selectorDef, common);
                    break;
                case "url":
                    scene.Url = EvaluateString(document, selectorDef, common);
                    break;
                case "image":
                    scene.Image = EvaluateString(document, selectorDef, common);
                    break;
                case "code":
                    scene.Code = EvaluateString(document, selectorDef, common);
                    break;
                case "director":
                    scene.Director = EvaluateString(document, selectorDef, common);
                    break;
                case "tags":
                    scene.Tags = EvaluateArray(document, selectorDef, common);
                    break;
                case "performers":
                    scene.Performers = EvaluateArray(document, selectorDef, common);
                    break;
                case "studio":
                    scene.Studio = EvaluateStudio(document, selectorDef, common);
                    break;
            }
        }

        return scene.HasData ? scene : null;
    }

    /// <summary>
    /// Evaluates a scene block in list mode for name searches: each scalar field selector is
    /// matched across all nodes and zipped by index into multiple result rows, mirroring how
    /// Stash turns a search-results page into candidate scenes.
    /// </summary>
    /// <param name="html">Raw search-results page HTML.</param>
    /// <param name="scraperBlock">The resolved scraper block.</param>
    /// <returns>Candidate scenes carrying title, url, date, image, and code where present.</returns>
    public IReadOnlyList<StashScrapedScene> EvaluateSceneList(string html, StashYamlNode scraperBlock) {
        var sceneDef = scraperBlock["scene"];
        if (sceneDef.IsMissing) {
            return [];
        }

        var document = Parser.ParseDocument(html);
        var common = scraperBlock["common"];

        var titles = ListFor(document, sceneDef, common, "Title");
        var urls = ListFor(document, sceneDef, common, "URL");
        var dates = ListFor(document, sceneDef, common, "Date");
        var images = ListFor(document, sceneDef, common, "Image");
        var codes = ListFor(document, sceneDef, common, "Code");

        var count = new[] { titles.Count, urls.Count }.Max();
        var results = new List<StashScrapedScene>(count);
        for (var index = 0; index < count; index++) {
            var scene = new StashScrapedScene {
                Title = At(titles, index),
                Url = At(urls, index),
                Date = At(dates, index),
                Image = At(images, index),
                Code = At(codes, index)
            };
            if (scene.HasData) {
                results.Add(scene);
            }
        }

        return results;
    }

    private List<string> ListFor(IDocument document, StashYamlNode sceneDef, StashYamlNode common, string field) {
        var node = sceneDef.HasKey(field) ? sceneDef[field] : sceneDef[field.ToLowerInvariant()];
        if (node.IsMissing) {
            return [];
        }

        var (selector, postProcess) = ParseFieldDef(node, common);
        if (string.IsNullOrWhiteSpace(selector)) {
            return [];
        }

        var values = new List<string>();
        foreach (var matched in SelectAll(document, selector)) {
            var raw = NodeValue(matched).Trim();
            if (string.IsNullOrEmpty(raw)) {
                continue;
            }

            var processed = StashSelector.ApplyPostProcess(raw, postProcess);
            if (!string.IsNullOrWhiteSpace(processed)) {
                values.Add(processed);
            }
        }

        return values;
    }

    private static string? At(IReadOnlyList<string> values, int index) =>
        index < values.Count ? values[index] : null;

    /// <summary>
    /// Evaluates a single string field (bare selector or selector + postProcess).
    /// </summary>
    public string? EvaluateString(IDocument document, StashYamlNode fieldDef, StashYamlNode common) {
        var (selector, postProcess) = ParseFieldDef(fieldDef, common);
        if (string.IsNullOrWhiteSpace(selector)) {
            return null;
        }

        var node = SelectSingle(document, selector);
        if (node is null) {
            return null;
        }

        var raw = NodeValue(node).Trim();
        if (string.IsNullOrEmpty(raw)) {
            return null;
        }

        var processed = StashSelector.ApplyPostProcess(raw, postProcess);
        return string.IsNullOrWhiteSpace(processed) ? null : processed;
    }

    private IReadOnlyList<string> EvaluateArray(IDocument document, StashYamlNode subObject, StashYamlNode common) {
        var nameField = subObject.HasKey("Name") ? subObject["Name"] : subObject["name"];
        if (nameField.IsMissing) {
            return [];
        }

        var (selector, postProcess) = ParseFieldDef(nameField, common);
        if (string.IsNullOrWhiteSpace(selector)) {
            return [];
        }

        var results = new List<string>();
        foreach (var node in SelectAll(document, selector)) {
            var raw = NodeValue(node).Trim();
            if (string.IsNullOrEmpty(raw)) {
                continue;
            }

            var processed = StashSelector.ApplyPostProcess(raw, postProcess);
            if (!string.IsNullOrWhiteSpace(processed)) {
                results.Add(processed);
            }
        }

        return results;
    }

    private StashScrapedStudio? EvaluateStudio(IDocument document, StashYamlNode subObject, StashYamlNode common) {
        var name = subObject.HasKey("Name") ? EvaluateString(document, subObject["Name"], common) : null;
        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        var url = subObject.HasKey("URL") ? EvaluateString(document, subObject["URL"], common)
            : subObject.HasKey("url") ? EvaluateString(document, subObject["url"], common)
            : null;
        return new StashScrapedStudio { Name = name, Url = url };
    }

    private static (string Selector, StashYamlNode PostProcess) ParseFieldDef(StashYamlNode fieldDef, StashYamlNode common) {
        if (fieldDef.Scalar is { } scalar) {
            return (StashSelector.ApplyCommon(scalar, common), MissingNode());
        }

        var selector = fieldDef.StringAt("selector") ?? string.Empty;
        return (StashSelector.ApplyCommon(selector, common), fieldDef["postProcess"]);
    }

    private static INode? SelectSingle(IDocument document, string xpath) {
        try {
            return document.DocumentElement.SelectSingleNode(xpath);
        } catch (Exception) {
            return null;
        }
    }

    private static IEnumerable<INode> SelectAll(IDocument document, string xpath) {
        try {
            return document.DocumentElement.SelectNodes(xpath).OfType<INode>();
        } catch (Exception) {
            return [];
        }
    }

    private static string NodeValue(INode node) =>
        node.NodeType == NodeType.Attribute
            ? node.NodeValue ?? string.Empty
            : node.TextContent ?? string.Empty;

    private static StashYamlNode MissingNode() => StashYamlNode.Parse(string.Empty);
}
