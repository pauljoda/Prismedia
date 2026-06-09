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

        // Kind eligibility comes from the synthesized manifest's declared Supports, not a hardcoded
        // EntityKind fork — so a scraper advertising performer/gallery capabilities is actually routed
        // to them, and the dotnet and stash runtimes both derive support from the manifest.
        if (!SupportsKind(descriptor.Manifest, request.Entity.Kind.ToCode())) {
            return new IdentifyPluginResponse(true, null, $"This scraper cannot identify '{request.Entity.Kind.ToCode()}'.");
        }

        var input = BuildInput(request, descriptor.Manifest.Id);
        var engine = new StashScraperEngine(_http, _scripts);
        try {
            return request.Entity.Kind == EntityKind.Person
                ? await IdentifyPersonAsync(engine, definition, descriptor, request, input, cancellationToken)
                : await IdentifySceneShapedAsync(engine, definition, descriptor, request, input, cancellationToken);
        } catch (StashScriptActionRequiredException) {
            return new IdentifyPluginResponse(
                false,
                null,
                $"Scraper '{definition.Name}' requires python script execution, which is not available yet.");
        }
    }

    /// <summary>Whether the synthesized manifest advertises support for the requested entity kind.</summary>
    private static bool SupportsKind(PluginManifest manifest, string kindCode) =>
        manifest.Supports.Any(support => PluginEntityKindCompatibility.SupportsKind(support, kindCode));

    /// <summary>
    /// Identifies a scene-shaped entity (video, movie, or gallery — all map to the common scene scrape
    /// shape). Honors the resolved action: a Lookup routes to the kind's by-URL capabilities; a Search
    /// routes to name search returning ranked candidates. Fragment capabilities (which template a URL
    /// from the title/filename and resolve to a single page) are tried as a confident fallback in both.
    /// </summary>
    private async Task<IdentifyPluginResponse> IdentifySceneShapedAsync(
        StashScraperEngine engine,
        StashScraperDefinition definition,
        PluginDescriptor descriptor,
        IdentifyPluginRequest request,
        StashScrapeInput input,
        CancellationToken cancellationToken) {
        var targetKind = request.Entity.Kind.ToProposalKind();
        var isLookup = request.Action is IdentifyAction.LookupUrl or IdentifyAction.LookupId;

        if (isLookup && !string.IsNullOrWhiteSpace(input.Url)) {
            foreach (var capability in UrlCapabilitiesFor(request.Entity.Kind).Where(definition.HasCapability)) {
                var proposal = await ScrapeProposalAsync(engine, definition, descriptor, capability, input, targetKind, cancellationToken);
                if (proposal is not null) {
                    return new IdentifyPluginResponse(true, proposal, null);
                }
            }
        }

        if (!isLookup && !string.IsNullOrWhiteSpace(input.Title) && definition.HasCapability("sceneByName")) {
            var scenes = await engine.SearchScenesAsync(definition, descriptor.EntryPath, "sceneByName", input, cancellationToken);
            var candidates = scenes
                .Select(scene => StashResultMapper.ToCandidate(scene, descriptor.Manifest.Id, input.Title))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .ToArray();
            if (candidates.Length > 0) {
                return IdentifyPluginResponse.Candidates(targetKind, candidates);
            }
        }

        // Fragment/query-fragment capabilities template a URL from the title or filename and resolve to
        // a single confident page — a deterministic fallback usable under either action.
        foreach (var capability in FragmentCapabilities.Where(definition.HasCapability)) {
            var proposal = await ScrapeProposalAsync(engine, definition, descriptor, capability, input, targetKind, cancellationToken);
            if (proposal is not null) {
                return new IdentifyPluginResponse(true, proposal, null);
            }
        }

        return new IdentifyPluginResponse(true, null, $"No {definition.Name} match was found.");
    }

    /// <summary>
    /// Identifies a performer: resolves the profile by URL (or, for python scrapers, by name) through
    /// the engine's performer path and maps it to a Person proposal.
    /// </summary>
    private async Task<IdentifyPluginResponse> IdentifyPersonAsync(
        StashScraperEngine engine,
        StashScraperDefinition definition,
        PluginDescriptor descriptor,
        IdentifyPluginRequest request,
        StashScrapeInput input,
        CancellationToken cancellationToken) {
        var performer = await engine.ResolvePerformerAsync(
            definition,
            descriptor.EntryPath,
            new StashScrapedPerformer { Name = input.Title, Url = input.Url },
            cancellationToken);
        if (performer is not { HasData: true }) {
            return new IdentifyPluginResponse(true, null, $"No {definition.Name} match was found.");
        }

        var byUrl = !string.IsNullOrWhiteSpace(input.Url);
        var proposal = StashResultMapper.ToPerformerProposal(
            performer,
            descriptor.Manifest.Id,
            descriptor.Manifest.Name,
            input.Url,
            byUrl ? "Matched by URL" : "Matched by name",
            byUrl ? 0.9m : 0.7m);
        return new IdentifyPluginResponse(true, proposal, null);
    }

    private static readonly string[] FragmentCapabilities = ["sceneByQueryFragment", "sceneByFragment"];

    private static async Task<EntityMetadataProposal?> ScrapeProposalAsync(
        StashScraperEngine engine,
        StashScraperDefinition definition,
        PluginDescriptor descriptor,
        string capability,
        StashScrapeInput input,
        ProposalKind targetKind,
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

    private static IReadOnlyList<string> UrlCapabilitiesFor(EntityKind entityKind) => entityKind switch {
        EntityKind.Movie => ["movieByURL", "sceneByURL"],
        EntityKind.Gallery => ["galleryByURL"],
        _ => ["sceneByURL"]
    };
}
