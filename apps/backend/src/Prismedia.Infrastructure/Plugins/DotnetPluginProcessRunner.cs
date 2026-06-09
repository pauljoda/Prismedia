using System.Text.Json;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Executes dotnet-process plugins as short-lived child processes.
/// </summary>
public sealed class DotnetPluginProcessRunner : IIdentifyRunner {
    /// <summary>Runtime code claimed by this runner.</summary>
    public const string RuntimeCode = "dotnet-process";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        // Codec enums (e.g. proposal TargetKind) round-trip as their stable string code on the
        // plugin wire, matching the HTTP contract a plugin author sees.
        Converters = { new CodecJsonConverterFactory() }
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
                ? ConvertWireResponse(wire, descriptor.Manifest.Name, request.Entity.Kind.ToProposalKind())
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
    /// <param name="Type">
    /// Plugin-authored result discriminator decoded against <see cref="IdentifyResultKind"/>. Kept as a
    /// raw string at this external decode boundary so an unknown/forward-compatible value falls through
    /// to the no-match path rather than throwing. // prism-vocab: external
    /// </param>
    private sealed record PluginWireResult(
        string? Type,
        EntityMetadataProposal? Proposal,
        IReadOnlyList<PluginWireSearchCandidate>? Candidates);

    private sealed record PluginWireResponse(bool Ok, PluginWireResult? Result, string? Error);

    private sealed record PluginWireSearchCandidate(
        string? CandidateId,
        IReadOnlyDictionary<string, string>? ExternalIds,
        string? Title,
        int? Year,
        string? Overview,
        string? PosterUrl,
        decimal? Popularity,
        string? Description,
        string? ThumbnailUrl,
        string? Source,
        decimal? Confidence,
        string? MatchReason);

    private static IdentifyPluginResponse ConvertWireResponse(PluginWireResponse wire, string providerName, ProposalKind targetKind) {
        if (!wire.Ok || wire.Result is null) {
            return new IdentifyPluginResponse(wire.Ok, null, wire.Error);
        }

        var result = wire.Result;
        if (string.Equals(result.Type, IdentifyResultKind.Proposal.ToCode(), StringComparison.OrdinalIgnoreCase)
            && result.Proposal is not null) {
            return new IdentifyPluginResponse(true, result.Proposal, wire.Error);
        }

        if (string.Equals(result.Type, IdentifyResultKind.Candidates.ToCode(), StringComparison.OrdinalIgnoreCase)
            && result.Candidates is { Count: > 0 }) {
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
                TargetKind: targetKind,
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
            candidate.Popularity,
            candidate.CandidateId,
            candidate.Source,
            candidate.Confidence,
            candidate.MatchReason);

    private static void TryDelete(string path) {
        try {
            File.Delete(path);
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }
}
