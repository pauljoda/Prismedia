using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Identify runner for Stash community scrapers. Parses the scraper YAML referenced by the
/// synthesized descriptor, maps the Prismedia action/kind to a Stash capability, runs the
/// XPath/JSON engine, and returns an <see cref="EntityMetadataProposal"/>.
/// </summary>
public sealed class StashCompatRunner : IIdentifyRunner {
    /// <summary>Runtime code claimed by this runner.</summary>
    public const string RuntimeCode = "stash-compat";

    private readonly HttpClient _http;
    private readonly StashScriptExecutor? _scripts;

    /// <summary>
    /// Creates the runner over an HTTP client and an optional python script executor.
    /// </summary>
    /// <param name="http">HTTP client (injected so tests can stub responses).</param>
    /// <param name="scripts">Python script executor; when null, python scrapers report unavailable.</param>
    public StashCompatRunner(HttpClient http, StashScriptExecutor? scripts = null) {
        _http = http;
        _scripts = scripts;
    }

    /// <inheritdoc />
    public bool CanRun(PluginDescriptor descriptor) =>
        descriptor.Manifest.Runtime.Equals(RuntimeCode, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IdentifyPluginResponse> IdentifyAsync(
        PluginDescriptor descriptor,
        IdentifyPluginRequest request,
        CancellationToken cancellationToken) {
        string yaml;
        try {
            yaml = await File.ReadAllTextAsync(descriptor.EntryPath, cancellationToken);
        } catch (IOException ex) {
            return new IdentifyPluginResponse(false, null, $"Failed to read scraper: {ex.Message}");
        }

        var definition = StashScraperDefinition.TryParse(yaml);
        if (definition is null) {
            return new IdentifyPluginResponse(false, null, "Scraper definition is invalid.");
        }

        var isVideo = request.Entity.Kind is EntityKind.Video or EntityKind.Movie;
        if (!isVideo) {
            return new IdentifyPluginResponse(true, null, $"This scraper cannot identify '{request.Entity.Kind.ToCode()}'.");
        }

        var input = BuildInput(request, descriptor.Manifest.Id);
        var engine = new StashScraperEngine(_http, _scripts);
        try {
            // A URL (direct, from a hint, or carried by a chosen search candidate) is the
            // strongest signal: resolve it to a single confident proposal.
            if (!string.IsNullOrWhiteSpace(input.Url)) {
                foreach (var capability in UrlCapabilitiesFor(request.Entity.Kind).Where(definition.HasCapability)) {
                    var proposal = await ScrapeProposalAsync(engine, definition, descriptor, capability, input, request.Entity.Kind.ToCode(), cancellationToken);
                    if (proposal is not null) {
                        return new IdentifyPluginResponse(true, proposal, null);
                    }
                }
            }

            // Fragment/query-fragment capabilities template a URL from the title or filename and
            // resolve to a single page, so they also yield a confident proposal.
            foreach (var capability in new[] { "sceneByQueryFragment", "sceneByFragment" }.Where(definition.HasCapability)) {
                var proposal = await ScrapeProposalAsync(engine, definition, descriptor, capability, input, request.Entity.Kind.ToCode(), cancellationToken);
                if (proposal is not null) {
                    return new IdentifyPluginResponse(true, proposal, null);
                }
            }

            // Name search returns multiple candidates for the user to disambiguate.
            if (!string.IsNullOrWhiteSpace(input.Title) && definition.HasCapability("sceneByName")) {
                var scenes = await engine.SearchScenesAsync(definition, descriptor.EntryPath, "sceneByName", input, cancellationToken);
                var candidates = scenes
                    .Select(scene => StashResultMapper.ToCandidate(scene, descriptor.Manifest.Id))
                    .Where(candidate => candidate is not null)
                    .Select(candidate => candidate!)
                    .ToArray();
                if (candidates.Length > 0) {
                    return new IdentifyPluginResponse(true, CandidatesShell(candidates), null);
                }
            }
        } catch (StashScriptActionRequiredException) {
            return new IdentifyPluginResponse(
                false,
                null,
                $"Scraper '{definition.Name}' requires python script execution, which is not available yet.");
        }

        return new IdentifyPluginResponse(true, null, $"No {definition.Name} match was found.");
    }

    private static async Task<EntityMetadataProposal?> ScrapeProposalAsync(
        StashScraperEngine engine,
        StashScraperDefinition definition,
        PluginDescriptor descriptor,
        string capability,
        StashScrapeInput input,
        string targetKind,
        CancellationToken cancellationToken) {
        var scene = await engine.ScrapeSceneAsync(definition, descriptor.EntryPath, capability, input, cancellationToken);
        if (scene is not { HasData: true }) {
            return null;
        }

        await EnrichPerformersAsync(engine, definition, descriptor.EntryPath, scene, cancellationToken);

        var byUrl = capability.EndsWith("ByURL", StringComparison.OrdinalIgnoreCase);
        return StashResultMapper.ToProposal(
            scene,
            descriptor.Manifest.Id,
            descriptor.Manifest.Name,
            targetKind,
            input.Url,
            byUrl ? "Matched by URL" : "Matched by query",
            byUrl ? 0.9m : 0.7m);
    }

    /// <summary>
    /// Maximum performers enriched per scene, bounding the extra network/script calls.
    /// </summary>
    private const int MaxEnrichedPerformers = 12;

    /// <summary>
    /// Fills in performer artwork and bio by resolving each credited performer's profile through
    /// the scraper's performer capabilities. Best-effort: a scene without performer URLs/lookups,
    /// or a failed lookup, simply leaves the credit with just its name.
    /// </summary>
    private static async Task EnrichPerformersAsync(
        StashScraperEngine engine,
        StashScraperDefinition definition,
        string scraperPath,
        StashScrapedScene scene,
        CancellationToken cancellationToken) {
        if (!definition.HasCapability("performerByURL") && !definition.HasCapability("performerByName")) {
            return;
        }

        foreach (var performer in scene.Performers.Take(MaxEnrichedPerformers)) {
            if (!string.IsNullOrWhiteSpace(performer.Image)) {
                continue;
            }

            StashScrapedPerformer? resolved;
            try {
                resolved = await engine.ResolvePerformerAsync(definition, scraperPath, performer, cancellationToken);
            } catch (StashScriptActionRequiredException) {
                return;
            } catch (Exception) when (!cancellationToken.IsCancellationRequested) {
                continue;
            }

            if (resolved is null) {
                continue;
            }

            performer.Image ??= resolved.Image;
            performer.Url ??= resolved.Url;
            performer.Details ??= resolved.Details;
            performer.Gender ??= resolved.Gender;
            performer.Birthdate ??= resolved.Birthdate;
            performer.Country ??= resolved.Country;
        }
    }

    /// <summary>
    /// Builds the candidate-only response shell, mirroring the dotnet runner so the identify
    /// pipeline routes it to the disambiguation UI rather than treating it as a confident match.
    /// </summary>
    private static EntityMetadataProposal CandidatesShell(IReadOnlyList<EntitySearchCandidate> candidates) =>
        new(
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

    private static StashScrapeInput BuildInput(IdentifyPluginRequest request, string providerId) {
        var url = FirstNonEmpty(
            request.Query.Url,
            CandidateUrl(request.Query.ExternalIds, providerId),
            request.Hints.Urls.FirstOrDefault());
        var title = !string.IsNullOrWhiteSpace(request.Query.Title)
            ? request.Query.Title
            : request.Hints.Title ?? request.Entity.Title;
        return new StashScrapeInput(
            Url: url,
            Title: title,
            FilePath: request.Hints.FilePath);
    }

    /// <summary>
    /// A chosen search candidate carries its scene URL in its external ids under the scraper id;
    /// recover it so the follow-up lookup resolves the full scene by URL.
    /// </summary>
    private static string? CandidateUrl(IReadOnlyDictionary<string, string>? externalIds, string providerId) {
        if (externalIds is null || !externalIds.TryGetValue(providerId, out var value)) {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out _) ? value : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<string> UrlCapabilitiesFor(EntityKind entityKind) =>
        entityKind == EntityKind.Movie
            ? ["movieByURL", "sceneByURL"]
            : ["sceneByURL"];
}
