using System.Net.Http.Headers;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Orchestrates a single Stash scraper lookup: resolves the action and fetch URL, fetches the
/// page with driver cookies, and dispatches to the XPath or JSON evaluator. Python <c>script</c>
/// actions are handled separately by the runner; this engine reports them as unsupported here.
/// </summary>
public sealed class StashScraperEngine {
    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly StashScriptExecutor? _scripts;
    private readonly StashXPathEngine _xpath;
    private readonly StashJsonEngine _json;

    /// <summary>
    /// Creates the engine over an HTTP client and an optional python script executor.
    /// </summary>
    /// <param name="http">HTTP client (injected so tests can stub responses).</param>
    /// <param name="scripts">Python script executor; when null, script actions are reported unsupported.</param>
    public StashScraperEngine(HttpClient http, StashScriptExecutor? scripts = null) {
        _http = http;
        _scripts = scripts;
        _xpath = new StashXPathEngine();
        _json = new StashJsonEngine();
    }

    /// <summary>
    /// Runs a scene-shaped capability (sceneByURL/sceneByName/sceneByFragment/sceneByQueryFragment).
    /// </summary>
    /// <param name="definition">Parsed scraper definition.</param>
    /// <param name="scraperPath">Absolute path to the scraper file (working dir for scripts).</param>
    /// <param name="capability">The Stash capability key to run.</param>
    /// <param name="input">Lookup inputs (URL, title, file path).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The scraped scene, or null when no match was produced.</returns>
    public async Task<StashScrapedScene?> ScrapeSceneAsync(
        StashScraperDefinition definition,
        string scraperPath,
        string capability,
        StashScrapeInput input,
        CancellationToken cancellationToken) {
        var action = definition.ResolveAction(capability, input.Url);
        if (action is null) {
            return null;
        }

        if (action.Kind == StashActionKind.Script) {
            if (_scripts is null) {
                throw new StashScriptActionRequiredException(definition.Name, capability, action);
            }

            try {
                return await _scripts.ScrapeSceneAsync(scraperPath, action, input, cancellationToken);
            } catch (StashPythonUnavailableException) {
                throw new StashScriptActionRequiredException(definition.Name, capability, action);
            }
        }

        var fetchUrl = ResolveFetchUrl(capability, action, input);
        if (string.IsNullOrWhiteSpace(fetchUrl) || string.IsNullOrWhiteSpace(action.ScraperKey)) {
            return null;
        }

        var body = await FetchAsync(definition, fetchUrl, cancellationToken);
        if (body is null) {
            return null;
        }

        return action.Kind switch {
            StashActionKind.ScrapeXPath => _xpath.EvaluateScene(body, definition.XPathScraper(action.ScraperKey)),
            StashActionKind.ScrapeJson => _json.EvaluateScene(body, definition.JsonScraper(action.ScraperKey)),
            _ => null
        };
    }

    /// <summary>
    /// Runs a name-search capability that yields multiple candidate scenes (sceneByName),
    /// building the search URL from the action's <c>queryURL</c> and the input title.
    /// </summary>
    /// <param name="definition">Parsed scraper definition.</param>
    /// <param name="capability">The Stash search capability key.</param>
    /// <param name="input">Lookup inputs (title drives the search URL).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Candidate scenes; empty when the scraper cannot search by name here.</returns>
    public async Task<IReadOnlyList<StashScrapedScene>> SearchScenesAsync(
        StashScraperDefinition definition,
        string scraperPath,
        string capability,
        StashScrapeInput input,
        CancellationToken cancellationToken) {
        var action = definition.ResolveAction(capability, inputUrl: null);
        if (action is null) {
            return [];
        }

        if (action.Kind == StashActionKind.Script) {
            if (_scripts is null) {
                throw new StashScriptActionRequiredException(definition.Name, capability, action);
            }

            try {
                return await _scripts.SearchScenesAsync(scraperPath, action, input, cancellationToken);
            } catch (StashPythonUnavailableException) {
                throw new StashScriptActionRequiredException(definition.Name, capability, action);
            }
        }

        if (string.IsNullOrWhiteSpace(action.QueryUrl) || string.IsNullOrWhiteSpace(action.ScraperKey)) {
            return [];
        }

        var fetchUrl = StashQueryUrl.Build(action.QueryUrl, action.QueryUrlReplace, input);
        if (string.IsNullOrWhiteSpace(fetchUrl)) {
            return [];
        }

        var body = await FetchAsync(definition, fetchUrl, cancellationToken);
        if (body is null) {
            return [];
        }

        return action.Kind == StashActionKind.ScrapeXPath
            ? _xpath.EvaluateSceneList(body, definition.XPathScraper(action.ScraperKey))
            : [];
    }

    private static string? ResolveFetchUrl(string capability, StashAction action, StashScrapeInput input) {
        if (capability.EndsWith("ByURL", StringComparison.OrdinalIgnoreCase)) {
            return input.Url;
        }

        if (!string.IsNullOrWhiteSpace(action.QueryUrl)) {
            return StashQueryUrl.Build(action.QueryUrl, action.QueryUrlReplace, input);
        }

        return input.Url;
    }

    private async Task<string?> FetchAsync(
        StashScraperDefinition definition,
        string url,
        CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(ChromeUserAgent);
        var cookieHeader = BuildCookieHeader(definition, url);
        if (!string.IsNullOrEmpty(cookieHeader)) {
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        try {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        } catch (HttpRequestException) {
            return null;
        } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return null;
        }
    }

    private static string? BuildCookieHeader(StashScraperDefinition definition, string fetchUrl) {
        if (!Uri.TryCreate(fetchUrl, UriKind.Absolute, out var fetchUri)) {
            return null;
        }

        var fetchHost = StripWww(fetchUri.Host);
        var pairs = new List<string>();
        foreach (var group in definition.DriverCookies.Items()) {
            var cookieUrl = group.StringAt("CookieURL");
            if (cookieUrl is null || !Uri.TryCreate(cookieUrl, UriKind.Absolute, out var cookieUri)) {
                continue;
            }

            if (!fetchHost.EndsWith(StripWww(cookieUri.Host), StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            foreach (var cookie in group["Cookies"].Items()) {
                var name = cookie.StringAt("Name");
                var value = cookie["Value"].Scalar;
                if (!string.IsNullOrEmpty(name) && value is not null) {
                    pairs.Add($"{name}={value}");
                }
            }
        }

        return pairs.Count > 0 ? string.Join("; ", pairs) : null;
    }

    private static string StripWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}

/// <summary>
/// Raised when a capability resolves to a python <c>script</c> action, which the HTTP engine
/// cannot run. The runner catches this to dispatch to the python executor (or to report that
/// python support is unavailable).
/// </summary>
public sealed class StashScriptActionRequiredException(string scraperName, string capability, StashAction action)
    : Exception($"Scraper '{scraperName}' uses a python script action for '{capability}'.") {
    /// <summary>The scraper that requires python.</summary>
    public string ScraperName { get; } = scraperName;

    /// <summary>The capability that resolved to a script action.</summary>
    public string Capability { get; } = capability;

    /// <summary>The resolved script action.</summary>
    public StashAction Action { get; } = action;
}
