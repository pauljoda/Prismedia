using System.Text.Json;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Executes .NET process plugins as short-lived child processes.
/// </summary>
public sealed class DotnetPluginProcessRunner : IIdentifyRunner {
    /// <summary>Manifest runtime code owned by this runner.</summary>
    public const string Code = "dotnet-process";

    private static readonly TimeSpan DefaultIdentifyTimeout = TimeSpan.FromSeconds(60);
    private readonly PluginProcessTransport _transport;
    private readonly TimeSpan _identifyTimeout;

    public DotnetPluginProcessRunner(ProcessExecutor processes, PluginCatalogOptions options, TimeSpan? identifyTimeout = null) {
        _transport = new PluginProcessTransport(processes, options);
        _identifyTimeout = identifyTimeout ?? DefaultIdentifyTimeout;
    }

    /// <inheritdoc />
    public string RuntimeCode => Code;

    /// <summary>
    /// Runs one identify request and parses the plugin response from stdout.
    /// </summary>
    public async Task<IdentifyPluginResponse> IdentifyAsync(
        PluginDescriptor descriptor,
        IdentifyPluginRequest request,
        CancellationToken cancellationToken) {
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_identifyTimeout);
            var result = await _transport.RunAsync(descriptor, request, timeout.Token);

            if (result.ExitCode != 0) {
                return new IdentifyPluginResponse(
                    false,
                    null,
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? $"Plugin exited with code {result.ExitCode}."
                        : PluginProcessTransport.RedactError(result.StandardError.Trim(), request.Auth.Values));
            }

            var wire = JsonSerializer.Deserialize<PluginWireResponse>(result.StandardOutput, PluginProcessTransport.JsonOptions);
            if (wire is not null) wire = wire with { Error = PluginProcessTransport.RedactError(wire.Error, request.Auth.Values) };
            return wire is not null
                ? ConvertWireResponse(wire, descriptor.Manifest.Name, request.Entity.Kind)
                : new IdentifyPluginResponse(false, null, "Plugin returned an empty response.");
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new IdentifyPluginResponse(false, null, $"Plugin timed out after {_identifyTimeout.TotalSeconds:0} seconds.");
        } catch (ProcessOutputLimitException) {
            return IdentifyPluginResponse.Failure("Plugin output exceeded its size limit.");
        } catch (InvalidDataException) {
            return IdentifyPluginResponse.Failure("Plugin request exceeded its size limit.");
        } catch (JsonException) {
            return IdentifyPluginResponse.Failure("Plugin returned invalid JSON or exceeded the nesting limit.");
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

    private static IdentifyPluginResponse ConvertWireResponse(PluginWireResponse wire, string providerName, EntityKind targetKind) {
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
                return IdentifyPluginResponse.NoMatch(wire.Error ?? $"No {providerName} match was found.");
            }

            return IdentifyPluginResponse.Candidates(targetKind, candidates, wire.Error);
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

}
