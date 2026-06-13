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

    /// <summary>
    /// Hard ceiling on one script invocation. Scraper scripts call external sites and can hang
    /// indefinitely on a stuck connection; without this bound a manual identify search never
    /// returns. Matches the dotnet plugin runner's identify timeout.
    /// </summary>
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(60);

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

    /// <summary>
    /// Runs a <c>performerByName</c> script action, returning candidate performers parsed from
    /// the script's JSON array output. The performer name is sent as the stdin fragment.
    /// </summary>
    public async Task<IReadOnlyList<StashScrapedPerformer>> SearchPerformersByNameAsync(
        string scraperPath,
        StashAction action,
        string name,
        CancellationToken cancellationToken) {
        var output = await RunRawAsync(scraperPath, action, SerializeNameFragment(name), cancellationToken);
        if (string.IsNullOrEmpty(output)) {
            return [];
        }

        try {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind == JsonValueKind.Array) {
                return document.RootElement.EnumerateArray()
                    .Select(ParsePerformer)
                    .Where(performer => performer is { HasData: true })
                    .Select(performer => performer!)
                    .ToArray();
            }

            var single = ParsePerformer(document.RootElement);
            return single is { HasData: true } ? [single] : [];
        } catch (JsonException) {
            return [];
        }
    }

    /// <summary>
    /// Runs a single-result tag script action.
    /// </summary>
    public async Task<StashScrapedTag?> ScrapeTagAsync(
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
            return ParseTag(document.RootElement);
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// Runs a <c>tagByName</c> script action, returning candidate tags parsed from stdout.
    /// </summary>
    public async Task<IReadOnlyList<StashScrapedTag>> SearchTagsByNameAsync(
        string scraperPath,
        StashAction action,
        string name,
        CancellationToken cancellationToken) {
        var output = await RunRawAsync(scraperPath, action, SerializeNameFragment(name), cancellationToken);
        if (string.IsNullOrEmpty(output)) {
            return [];
        }

        try {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind == JsonValueKind.Array) {
                return document.RootElement.EnumerateArray()
                    .Select(ParseTag)
                    .Where(tag => tag is { HasData: true })
                    .Select(tag => tag!)
                    .ToArray();
            }

            var single = ParseTag(document.RootElement);
            return single is { HasData: true } ? [single] : [];
        } catch (JsonException) {
            return [];
        }
    }

    /// <summary>
    /// Runs a single-result studio script action.
    /// </summary>
    public async Task<StashScrapedStudio?> ScrapeStudioAsync(
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
            return ParseStudio(document.RootElement);
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// Runs a <c>studioByName</c> script action, returning candidate studios parsed from stdout.
    /// </summary>
    public async Task<IReadOnlyList<StashScrapedStudio>> SearchStudiosByNameAsync(
        string scraperPath,
        StashAction action,
        string name,
        CancellationToken cancellationToken) {
        var output = await RunRawAsync(scraperPath, action, SerializeNameFragment(name), cancellationToken);
        if (string.IsNullOrEmpty(output)) {
            return [];
        }

        try {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind == JsonValueKind.Array) {
                return document.RootElement.EnumerateArray()
                    .Select(ParseStudio)
                    .Where(studio => studio is { HasData: true })
                    .Select(studio => studio!)
                    .ToArray();
            }

            var single = ParseStudio(document.RootElement);
            return single is { HasData: true } ? [single] : [];
        } catch (JsonException) {
            return [];
        }
    }

    private async Task<string?> RunAsync(
        string scraperPath,
        StashAction action,
        StashScrapeInput input,
        CancellationToken cancellationToken) =>
        await RunRawAsync(scraperPath, action, SerializeFragment(input), cancellationToken);

    private async Task<string?> RunRawAsync(
        string scraperPath,
        StashAction action,
        string stdin,
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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ScriptTimeout);

        ProcessExecutionResult result;
        try {
            result = await _processes.RunWithStdinAsync(
                command,
                arguments,
                stdin,
                environment,
                scraperDir,
                timeout.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException(
                $"Scraper script '{command}' timed out after {ScriptTimeout.TotalSeconds:0} seconds.");
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
        return JsonSerializer.Serialize(fragment, JsonOptions);
    }

    private static string SerializeNameFragment(string name) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = name }, JsonOptions);

    private static StashScrapedPerformer? ParsePerformer(JsonElement element) {
        if (element.ValueKind != JsonValueKind.Object) {
            return null;
        }

        return new StashScrapedPerformer {
            Name = StringField(element, "name"),
            Url = StringField(element, "url") ?? FirstString(element, "urls"),
            Image = StringField(element, "image") ?? FirstString(element, "images"),
            Gender = StringField(element, "gender"),
            Details = StringField(element, "details"),
            Birthdate = StringField(element, "birthdate"),
            Country = StringField(element, "country")
        };
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
            Performers = PerformerArray(element, "performers"),
            Tags = TagArray(element, "tags")
        };

        if (element.TryGetProperty("studio", out var studio) && studio.ValueKind == JsonValueKind.Object) {
            scene.Studio = ParseStudio(studio);
        }

        return scene;
    }

    private static StashScrapedStudio? ParseStudio(JsonElement element) {
        if (element.ValueKind == JsonValueKind.String) {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : new StashScrapedStudio { Name = value.Trim() };
        }

        if (element.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var studio = new StashScrapedStudio {
            Name = StringField(element, "name"),
            Url = StringField(element, "url") ?? FirstString(element, "urls"),
            Image = StringField(element, "image") ?? FirstString(element, "images"),
            Description = StringField(element, "description") ?? StringField(element, "details")
        };
        return studio.HasData ? studio : null;
    }

    private static StashScrapedTag? ParseTag(JsonElement element) {
        if (element.ValueKind == JsonValueKind.String) {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : new StashScrapedTag { Name = value.Trim() };
        }

        if (element.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var tag = new StashScrapedTag {
            Name = StringField(element, "name"),
            Url = StringField(element, "url") ?? FirstString(element, "urls"),
            Image = StringField(element, "image") ?? FirstString(element, "images"),
            Description = StringField(element, "description") ?? StringField(element, "details"),
            Aliases = StringField(element, "aliases") ?? JoinedStrings(element, "aliases")
        };
        return tag.HasData ? tag : null;
    }

    private static IReadOnlyList<StashScrapedPerformer> PerformerArray(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var results = new List<StashScrapedPerformer>();
        foreach (var item in array.EnumerateArray()) {
            if (item.ValueKind == JsonValueKind.String) {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    results.Add(new StashScrapedPerformer { Name = value.Trim() });
                }
            } else if (item.ValueKind == JsonValueKind.Object) {
                var performerName = StringField(item, "name");
                if (!string.IsNullOrWhiteSpace(performerName)) {
                    results.Add(new StashScrapedPerformer {
                        Name = performerName.Trim(),
                        Url = StringField(item, "url") ?? FirstString(item, "urls"),
                        Image = StringField(item, "image") ?? FirstString(item, "images"),
                        Gender = StringField(item, "gender"),
                        Details = StringField(item, "details"),
                        Birthdate = StringField(item, "birthdate"),
                        Country = StringField(item, "country")
                    });
                }
            }
        }

        return results;
    }

    private static string? StringField(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? FirstString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.String).GetString()
            : null;

    private static IReadOnlyList<StashScrapedTag> TagArray(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var results = new List<StashScrapedTag>();
        foreach (var item in array.EnumerateArray()) {
            var tag = ParseTag(item);
            if (tag is { HasData: true }) {
                results.Add(tag);
            }
        }

        return results;
    }

    private static string? JoinedStrings(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) {
            return null;
        }

        var values = value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
        return values.Length == 0 ? null : string.Join(", ", values);
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
