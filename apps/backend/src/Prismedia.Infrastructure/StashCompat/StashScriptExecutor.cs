using System.ComponentModel;
using System.Text.Json;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Executes Stash python <c>script</c> scraper actions over the stdin/stdout JSON protocol.
/// The scene fragment is written to stdin as JSON, the script's JSON result is read from stdout,
/// and <c>PYTHONPATH</c> is set to the scrapers root so shared modules such as <c>py_common</c>
/// resolve. Exit code 69 (uncaught python exception) is treated as "no result".
/// </summary>
public sealed class StashScriptExecutor {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ProcessExecutor _processes;

    /// <summary>
    /// Creates the executor over the shared process runner.
    /// </summary>
    /// <param name="processes">Process executor used to spawn python.</param>
    public StashScriptExecutor(ProcessExecutor processes) {
        _processes = processes;
    }

    /// <summary>
    /// Runs a single-result scene script action.
    /// </summary>
    /// <param name="scraperPath">Absolute path to the scraper YAML (its directory is the working dir).</param>
    /// <param name="action">The resolved script action.</param>
    /// <param name="input">Lookup inputs serialized to the stdin fragment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The scraped scene, or null when the script returned no result.</returns>
    /// <exception cref="StashPythonUnavailableException">Python could not be launched.</exception>
    public async Task<StashScrapedScene?> ScrapeSceneAsync(
        string scraperPath,
        StashAction action,
        StashScrapeInput input,
        CancellationToken cancellationToken) {
        var output = await RunAsync(scraperPath, action, input, cancellationToken);
        if (string.IsNullOrEmpty(output)) {
            return null;
        }

        try {
            using var document = JsonDocument.Parse(output);
            return ParseScene(document.RootElement);
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// Runs a name-search script action that returns an array of candidate scenes.
    /// </summary>
    public async Task<IReadOnlyList<StashScrapedScene>> SearchScenesAsync(
        string scraperPath,
        StashAction action,
        StashScrapeInput input,
        CancellationToken cancellationToken) {
        var output = await RunAsync(scraperPath, action, input, cancellationToken);
        if (string.IsNullOrEmpty(output)) {
            return [];
        }

        try {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Array) {
                var single = ParseScene(document.RootElement);
                return single is { HasData: true } ? [single] : [];
            }

            return document.RootElement.EnumerateArray()
                .Select(ParseScene)
                .Where(scene => scene is { HasData: true })
                .Select(scene => scene!)
                .ToArray();
        } catch (JsonException) {
            return [];
        }
    }

    private async Task<string?> RunAsync(
        string scraperPath,
        StashAction action,
        StashScrapeInput input,
        CancellationToken cancellationToken) {
        if (action.Script.Count == 0) {
            return null;
        }

        var scraperDir = Path.GetDirectoryName(scraperPath) ?? Directory.GetCurrentDirectory();
        var command = action.Script[0];
        var arguments = action.Script.Skip(1).ToArray();
        var environment = new Dictionary<string, string> {
            ["PYTHONPATH"] = BuildPythonPath(scraperDir)
        };

        ProcessExecutionResult result;
        try {
            result = await _processes.RunWithStdinAsync(
                command,
                arguments,
                SerializeFragment(input),
                environment,
                scraperDir,
                cancellationToken);
        } catch (Win32Exception ex) {
            throw new StashPythonUnavailableException(command, ex);
        } catch (InvalidOperationException ex) {
            throw new StashPythonUnavailableException(command, ex);
        }

        // Exit code 69 signals an uncaught python exception; Stash treats that as no result.
        if (result.ExitCode is not (0 or 69)) {
            return null;
        }

        var trimmed = result.StandardOutput.Trim();
        return trimmed is "" or "null" ? null : trimmed;
    }

    private static string BuildPythonPath(string scraperDir) {
        var root = Path.GetDirectoryName(scraperDir) ?? scraperDir;
        var existing = Environment.GetEnvironmentVariable("PYTHONPATH");
        return string.IsNullOrEmpty(existing) ? root : $"{root}{Path.PathSeparator}{existing}";
    }

    private static string SerializeFragment(StashScrapeInput input) {
        var fragment = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(input.Url)) fragment["url"] = input.Url;
        if (!string.IsNullOrWhiteSpace(input.Title)) fragment["title"] = input.Title;
        if (!string.IsNullOrWhiteSpace(input.FilePath)) fragment["file_path"] = input.FilePath;
        if (!string.IsNullOrWhiteSpace(input.Checksum)) fragment["checksum"] = input.Checksum;
        if (!string.IsNullOrWhiteSpace(input.Oshash)) fragment["oshash"] = input.Oshash;
        if (!string.IsNullOrWhiteSpace(input.Phash)) fragment["phash"] = input.Phash;
        return JsonSerializer.Serialize(fragment, JsonOptions);
    }

    private static StashScrapedScene? ParseScene(JsonElement element) {
        if (element.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var scene = new StashScrapedScene {
            Title = StringField(element, "title"),
            Code = StringField(element, "code"),
            Details = StringField(element, "details"),
            Director = StringField(element, "director"),
            Date = StringField(element, "date"),
            Image = StringField(element, "image"),
            Url = StringField(element, "url") ?? FirstString(element, "urls"),
            Performers = NameArray(element, "performers"),
            Tags = NameArray(element, "tags")
        };

        if (element.TryGetProperty("studio", out var studio) && studio.ValueKind == JsonValueKind.Object) {
            var name = StringField(studio, "name");
            if (!string.IsNullOrWhiteSpace(name)) {
                scene.Studio = new StashScrapedStudio { Name = name, Url = StringField(studio, "url") };
            }
        }

        return scene;
    }

    private static string? StringField(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? FirstString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.String).GetString()
            : null;

    private static IReadOnlyList<string> NameArray(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var results = new List<string>();
        foreach (var item in array.EnumerateArray()) {
            var value = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.ValueKind == JsonValueKind.Object ? StringField(item, "name") : null;
            if (!string.IsNullOrWhiteSpace(value)) {
                results.Add(value!.Trim());
            }
        }

        return results;
    }
}

/// <summary>
/// Raised when a python scraper script cannot be launched because python is unavailable.
/// </summary>
public sealed class StashPythonUnavailableException(string command, Exception inner)
    : Exception($"Failed to launch '{command}'. Python may not be available.", inner) {
    /// <summary>The command that could not be started.</summary>
    public string Command { get; } = command;
}
