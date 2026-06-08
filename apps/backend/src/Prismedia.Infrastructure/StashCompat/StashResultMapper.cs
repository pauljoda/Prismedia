using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Maps a scraped Stash scene into a Prismedia <see cref="EntityMetadataProposal"/> that the
/// existing apply pipeline consumes directly: performers become credits, the studio name and
/// tag names become relationship fields, the date becomes a <c>release</c> date, and the cover
/// becomes a poster image candidate.
/// </summary>
public static class StashResultMapper {
    /// <summary>
    /// Builds a full proposal from a single scraped scene.
    /// </summary>
    /// <param name="scene">The scraped scene.</param>
    /// <param name="providerId">Provider/scraper id (stable slug).</param>
    /// <param name="providerName">Human-readable scraper name.</param>
    /// <param name="targetKind">Prismedia entity kind being identified.</param>
    /// <param name="inputUrl">The URL that was looked up, preserved on the entity.</param>
    /// <param name="matchReason">Why this matched (e.g. "Matched by URL").</param>
    /// <param name="confidence">Confidence score for the match.</param>
    /// <returns>A proposal carrying the scene's metadata patch and images.</returns>
    public static EntityMetadataProposal ToProposal(
        StashScrapedScene scene,
        string providerId,
        string providerName,
        ProposalKind targetKind,
        string? inputUrl,
        string matchReason,
        decimal confidence) {
        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(scene.Code)) {
            externalIds[providerId] = scene.Code.Trim();
        }

        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(scene.Url)) {
            urls.Add(scene.Url.Trim());
        }

        if (!string.IsNullOrWhiteSpace(inputUrl)) {
            urls.Add(inputUrl.Trim());
        }

        var dates = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(scene.Date)) {
            dates["release"] = scene.Date.Trim();
        }

        // Performers become "performer" credits; a director, when present, becomes a "director"
        // credit so the crew is captured too.
        var performers = scene.Performers
            .Where(performer => !string.IsNullOrWhiteSpace(performer.Name))
            .ToArray();
        var credits = new List<CreditPatch>();
        credits.AddRange(performers
            .Select((performer, index) => new CreditPatch(performer.Name!.Trim(), "performer", null, index)));
        if (!string.IsNullOrWhiteSpace(scene.Director)) {
            credits.Add(new CreditPatch(scene.Director.Trim(), "director", null, credits.Count));
        }

        var tags = scene.Tags
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToArray();

        var studioName = string.IsNullOrWhiteSpace(scene.Studio?.Name) ? null : scene.Studio!.Name!.Trim();

        var patch = new EntityMetadataPatch(
            string.IsNullOrWhiteSpace(scene.Title) ? null : scene.Title.Trim(),
            string.IsNullOrWhiteSpace(scene.Details) ? null : scene.Details.Trim(),
            externalIds,
            urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            tags,
            studioName,
            credits,
            dates,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            null);

        // A scraped scene is a direct video, so its cover is a wide thumbnail (the video's primary
        // display image) rather than a portrait poster, which is reserved for movies and series.
        var images = new List<ImageCandidate>();
        if (!string.IsNullOrWhiteSpace(scene.Image)) {
            images.Add(new ImageCandidate("thumbnail", scene.Image.Trim(), providerName, null, null, null, null));
        }

        // Emit credited people and the studio as relationship proposals too — not just flat patch
        // strings — so the review UI surfaces them as cards and the apply pipeline can enrich the
        // resulting Person/Studio entities (matching how first-party providers shape their output).
        var relationships = BuildRelationships(performers, scene.Director, scene.Studio, providerId, providerName);

        return new EntityMetadataProposal(
            $"{providerId}:{inputUrl ?? scene.Url ?? scene.Title}",
            providerName,
            targetKind,
            confidence,
            matchReason,
            patch,
            images,
            [],
            [],
            Relationships: relationships);
    }

    private static IReadOnlyList<EntityMetadataProposal> BuildRelationships(
        IReadOnlyList<StashScrapedPerformer> performers,
        string? director,
        StashScrapedStudio? studio,
        string providerId,
        string providerName) {
        var relationships = new List<EntityMetadataProposal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var performer in performers) {
            var name = performer.Name!.Trim();
            if (!seen.Add(name)) {
                continue;
            }

            relationships.Add(PersonProposal(providerId, providerName, name, performer));
        }

        if (!string.IsNullOrWhiteSpace(director) && seen.Add(director.Trim())) {
            relationships.Add(PersonProposal(providerId, providerName, director.Trim(), performer: null));
        }

        if (!string.IsNullOrWhiteSpace(studio?.Name)) {
            var studioName = studio!.Name!.Trim();
            var studioUrls = Uri.TryCreate(studio.Url, UriKind.Absolute, out _) ? new[] { studio.Url!.Trim() } : [];
            var studioImages = string.IsNullOrWhiteSpace(studio.Image)
                ? Array.Empty<ImageCandidate>()
                : [new ImageCandidate("logo", studio.Image.Trim(), providerName, null, null, null, null)];
            relationships.Add(RelationshipProposal($"{providerId}:studio:{studioName}", providerName, ProposalKind.Studio, studioName, null, studioUrls, studioImages));
        }

        return relationships;
    }

    private static EntityMetadataProposal PersonProposal(
        string providerId,
        string providerName,
        string name,
        StashScrapedPerformer? performer) {
        var urls = Uri.TryCreate(performer?.Url, UriKind.Absolute, out _) ? new[] { performer!.Url!.Trim() } : [];
        var images = string.IsNullOrWhiteSpace(performer?.Image)
            ? Array.Empty<ImageCandidate>()
            : [new ImageCandidate("poster", performer!.Image!.Trim(), providerName, null, null, null, null)];
        var details = string.IsNullOrWhiteSpace(performer?.Details) ? null : performer!.Details!.Trim();
        var dates = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(performer?.Birthdate)) {
            dates["birth"] = performer!.Birthdate!.Trim();
        }

        return RelationshipProposal($"{providerId}:person:{name}", providerName, ProposalKind.Person, name, details, urls, images, dates);
    }

    private static EntityMetadataProposal RelationshipProposal(
        string proposalId,
        string providerName,
        ProposalKind targetKind,
        string title,
        string? description,
        IReadOnlyList<string> urls,
        IReadOnlyList<ImageCandidate> images,
        IReadOnlyDictionary<string, string>? dates = null) =>
        new(
            proposalId,
            providerName,
            targetKind,
            null,
            "cascade",
            new EntityMetadataPatch(
                title,
                description,
                new Dictionary<string, string>(),
                urls,
                [],
                null,
                [],
                dates ?? new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                null),
            images,
            [],
            [],
            Relationships: []);

    /// <summary>
    /// Builds a candidate from a scraped scene for name-search disambiguation.
    /// </summary>
    /// <param name="scene">The scraped scene.</param>
    /// <param name="providerId">Provider/scraper id used as the external-id key.</param>
    /// <returns>A search candidate, or null when the scene has no title or locator.</returns>
    public static EntitySearchCandidate? ToCandidate(StashScrapedScene scene, string providerId) {
        if (string.IsNullOrWhiteSpace(scene.Title)) {
            return null;
        }

        // Carry the scene URL under the provider id so picking this candidate re-issues a
        // by-URL lookup that resolves the full scene; fall back to the studio code otherwise.
        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Uri.TryCreate(scene.Url, UriKind.Absolute, out _)) {
            externalIds[providerId] = scene.Url!.Trim();
        } else if (!string.IsNullOrWhiteSpace(scene.Code)) {
            externalIds[providerId] = scene.Code.Trim();
        }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(scene.Date) && scene.Date.Length >= 4 &&
            int.TryParse(scene.Date.AsSpan(0, 4), out var parsedYear)) {
            year = parsedYear;
        }

        return new EntitySearchCandidate(
            externalIds,
            scene.Title.Trim(),
            year,
            string.IsNullOrWhiteSpace(scene.Details) ? null : scene.Details.Trim(),
            string.IsNullOrWhiteSpace(scene.Image) ? null : scene.Image.Trim(),
            null);
    }
}
