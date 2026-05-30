namespace Prismedia.Infrastructure.StashCompat.Model;

/// <summary>
/// Typed accessor over a parsed Stash community scraper definition.
/// Wraps a <see cref="StashYamlNode"/> graph and exposes the pieces the
/// engine needs: the scraper name, declared capabilities, resolved action
/// definitions, named XPath/JSON scraper blocks, and driver cookies.
/// </summary>
public sealed class StashScraperDefinition {
    /// <summary>All Stash capability keys recognized by Prismedia.</summary>
    public static readonly IReadOnlyList<string> CapabilityKeys = [
        "sceneByURL",
        "sceneByName",
        "sceneByFragment",
        "sceneByQueryFragment",
        "performerByURL",
        "performerByName",
        "performerByFragment",
        "galleryByURL",
        "galleryByFragment",
        "movieByURL",
        "groupByURL"
    ];

    private readonly StashYamlNode _root;

    private StashScraperDefinition(StashYamlNode root) {
        _root = root;
    }

    /// <summary>Human-readable scraper name from the YAML <c>name</c> field.</summary>
    public string Name { get; private init; } = string.Empty;

    /// <summary>
    /// Parses a Stash scraper definition from raw YAML text.
    /// </summary>
    /// <param name="yaml">Raw YAML scraper definition.</param>
    /// <returns>The parsed definition, or null when the YAML lacks a required <c>name</c>.</returns>
    public static StashScraperDefinition? TryParse(string yaml) {
        var root = StashYamlNode.Parse(yaml);
        var name = root.StringAt("name");
        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return new StashScraperDefinition(root) { Name = name };
    }

    /// <summary>Returns the declared capability keys present in this definition.</summary>
    public IReadOnlyList<string> Capabilities() =>
        CapabilityKeys.Where(key => !_root[key].IsMissing).ToArray();

    /// <summary>True when the scraper declares the given capability.</summary>
    public bool HasCapability(string capability) => !_root[capability].IsMissing;

    /// <summary>True when any declared capability uses the python <c>script</c> action.</summary>
    public bool RequiresPython() =>
        Capabilities()
            .Select(capability => ResolveAction(capability, inputUrl: null))
            .Any(action => action is { Kind: StashActionKind.Script });

    /// <summary>The driver cookie groups declared by this scraper.</summary>
    public StashYamlNode DriverCookies => _root["driver"]["cookies"];

    /// <summary>The named XPath/JSON scraper block referenced by an action's <c>scraper</c> field.</summary>
    public StashYamlNode XPathScraper(string name) => _root["xPathScrapers"][name];

    /// <summary>The named JSON scraper block.</summary>
    public StashYamlNode JsonScraper(string name) => _root["jsonScrapers"][name];

    /// <summary>
    /// Resolves the action definition for a capability, honoring URL-keyed action arrays.
    /// Mirrors the reference engine's <c>resolveActionDef</c>: when the capability is a list,
    /// the entry whose <c>url</c> prefixes match the input wins, falling back to the generic
    /// (url-less) entry, then the first valid entry.
    /// </summary>
    /// <param name="capability">Capability key such as <c>sceneByURL</c>.</param>
    /// <param name="inputUrl">The URL being looked up, when known.</param>
    /// <returns>The resolved action, or null when no usable definition exists.</returns>
    public StashAction? ResolveAction(string capability, string? inputUrl) {
        var entry = _root[capability];
        if (entry.IsMissing) {
            return null;
        }

        if (entry.IsMap) {
            return StashAction.FromNode(entry);
        }

        if (!entry.IsList) {
            return null;
        }

        var candidates = entry.Items().Select(StashAction.FromNode).Where(action => action is not null).ToArray();
        if (!string.IsNullOrWhiteSpace(inputUrl)) {
            var matched = candidates.FirstOrDefault(action =>
                action!.UrlPatterns.Any(pattern => inputUrl.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
            if (matched is not null) {
                return matched;
            }

            var generic = candidates.FirstOrDefault(action => action!.UrlPatterns.Count == 0);
            if (generic is not null) {
                return generic;
            }
        }

        return candidates.FirstOrDefault();
    }
}

/// <summary>The execution engine implied by a Stash action's <c>action</c> field.</summary>
public enum StashActionKind {
    /// <summary>HTML scraping via XPath selectors.</summary>
    ScrapeXPath,

    /// <summary>JSON scraping via dotted/GJSON-style paths.</summary>
    ScrapeJson,

    /// <summary>External python script over stdin/stdout JSON.</summary>
    Script,

    /// <summary>Unsupported or unrecognized action.</summary>
    Unknown
}

/// <summary>
/// A single resolved Stash action definition (e.g. one <c>sceneByURL</c> entry).
/// </summary>
public sealed class StashAction {
    /// <summary>The engine this action runs through.</summary>
    public StashActionKind Kind { get; private init; }

    /// <summary>URL substrings that scope a URL-keyed action entry; empty for generic entries.</summary>
    public IReadOnlyList<string> UrlPatterns { get; private init; } = [];

    /// <summary>Name of the referenced <c>xPathScrapers</c>/<c>jsonScrapers</c> block, when applicable.</summary>
    public string? ScraperKey { get; private init; }

    /// <summary>URL template for fragment/query lookups, e.g. <c>https://site/video/{filename}</c>.</summary>
    public string? QueryUrl { get; private init; }

    /// <summary>Placeholder regex replacement rules applied to <see cref="QueryUrl"/>.</summary>
    public StashYamlNode QueryUrlReplace { get; private init; } = default!;

    /// <summary>Script command tokens for <see cref="StashActionKind.Script"/> actions.</summary>
    public IReadOnlyList<string> Script { get; private init; } = [];

    /// <summary>
    /// Builds a typed action from a YAML node, or null when the node has no <c>action</c>.
    /// </summary>
    public static StashAction? FromNode(StashYamlNode node) {
        var action = node.StringAt("action");
        if (string.IsNullOrWhiteSpace(action)) {
            return null;
        }

        return new StashAction {
            Kind = action.ToLowerInvariant() switch {
                "scrapexpath" => StashActionKind.ScrapeXPath,
                "scrapejson" => StashActionKind.ScrapeJson,
                "script" => StashActionKind.Script,
                _ => StashActionKind.Unknown
            },
            UrlPatterns = node["url"].Items()
                .Select(item => item.Scalar)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray(),
            ScraperKey = node.StringAt("scraper"),
            QueryUrl = node.StringAt("queryURL"),
            QueryUrlReplace = node["queryURLReplace"],
            Script = node["script"].Items()
                .Select(item => item.Scalar)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray()
        };
    }
}
