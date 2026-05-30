using System.Text.Json;
using Prismedia.Contracts.Plugins;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Executes dotnet-process plugins as short-lived child processes.
/// </summary>
public sealed class DotnetPluginProcessRunner : IIdentifyRunner {
    /// <summary>Runtime code claimed by this runner.</summary>
    public const string RuntimeCode = "dotnet-process";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ProcessExecutor _processes;
    private readonly PluginCatalogOptions _options;

    public DotnetPluginProcessRunner(ProcessExecutor processes, PluginCatalogOptions options) {
        _processes = processes;
        _options = options;
    }

    /// <inheritdoc />
    public bool CanRun(PluginDescriptor descriptor) =>
        descriptor.Manifest.Runtime.Equals(RuntimeCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs one identify request and parses the plugin response from stdout.
    /// </summary>
    public async Task<IdentifyPluginResponse> IdentifyAsync(
        PluginDescriptor descriptor,
        IdentifyPluginRequest request,
        CancellationToken cancellationToken) {
        var requestDirectory = Path.Combine(_options.CacheRoot, "plugins", "requests");
        Directory.CreateDirectory(requestDirectory);
        var requestPath = Path.Combine(requestDirectory, $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);

        try {
            var result = await _processes.RunAsync(
                "dotnet",
                [descriptor.EntryPath, requestPath],
                environment: null,
                cancellationToken);

            if (result.ExitCode != 0) {
                return new IdentifyPluginResponse(
                    false,
                    null,
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? $"Plugin exited with code {result.ExitCode}."
                        : result.StandardError.Trim());
            }

            var wire = JsonSerializer.Deserialize<PluginWireResponse>(result.StandardOutput, JsonOptions);
            return wire is not null
                ? ConvertWireResponse(wire, descriptor.Manifest.Name)
                : new IdentifyPluginResponse(false, null, "Plugin returned an empty response.");
        } catch (JsonException ex) {
            return new IdentifyPluginResponse(false, null, $"Plugin returned invalid JSON: {ex.Message}");
        } finally {
            TryDelete(requestPath);
        }
    }

    /// <summary>
    /// Wire format matching the plugin's IdentifyPluginResult nested inside its response envelope.
    /// </summary>
    private sealed record PluginWireResult(
        string? Type,
        EntityMetadataProposal? Proposal,
        IReadOnlyList<PluginWireSearchCandidate>? Candidates);

    private sealed record PluginWireResponse(bool Ok, PluginWireResult? Result, string? Error);

    private sealed record PluginWireSearchCandidate(
        IReadOnlyDictionary<string, string>? ExternalIds,
        string? Title,
        int? Year,
        string? Overview,
        string? PosterUrl,
        decimal? Popularity,
        string? Description,
        string? ThumbnailUrl);

    private static IdentifyPluginResponse ConvertWireResponse(PluginWireResponse wire, string providerName) {
        if (!wire.Ok || wire.Result is null) {
            return new IdentifyPluginResponse(wire.Ok, null, wire.Error);
        }

        var result = wire.Result;
        if (result.Type == "proposal" && result.Proposal is not null) {
            return new IdentifyPluginResponse(true, result.Proposal, wire.Error);
        }

        if (result.Type == "candidates" && result.Candidates is { Count: > 0 }) {
            var candidates = result.Candidates
                .Select(NormalizeSearchCandidate)
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Title))
                .ToArray();
            if (candidates.Length == 0) {
                return new IdentifyPluginResponse(true, null, wire.Error ?? $"No {providerName} match was found.");
            }

            var shell = new EntityMetadataProposal(
                ProposalId: null!,
                Provider: null!,
                TargetKind: null!,
                Confidence: null,
                MatchReason: null,
                Patch: null!,
                Images: [],
                Children: [],
                Candidates: candidates,
                TargetEntityId: null,
                Relationships: []);
            return new IdentifyPluginResponse(true, shell, wire.Error);
        }

        return new IdentifyPluginResponse(true, null, wire.Error ?? $"No {providerName} match was found.");
    }

    private static EntitySearchCandidate NormalizeSearchCandidate(PluginWireSearchCandidate candidate) =>
        new(
            candidate.ExternalIds ?? new Dictionary<string, string>(),
            candidate.Title ?? string.Empty,
            candidate.Year,
            candidate.Overview ?? candidate.Description,
            candidate.PosterUrl ?? candidate.ThumbnailUrl,
            candidate.Popularity);

    private static void TryDelete(string path) {
        try {
            File.Delete(path);
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }
}
