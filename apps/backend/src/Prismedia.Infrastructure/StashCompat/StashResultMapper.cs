using Prismedia.Contracts.Plugins;
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
        string targetKind,
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

        var credits = scene.Performers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select((name, index) => new CreditPatch(name.Trim(), "performer", null, index))
            .ToArray();

        var tags = scene.Tags
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToArray();

        var patch = new EntityMetadataPatch(
            string.IsNullOrWhiteSpace(scene.Title) ? null : scene.Title.Trim(),
            string.IsNullOrWhiteSpace(scene.Details) ? null : scene.Details.Trim(),
            externalIds,
            urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            tags,
            string.IsNullOrWhiteSpace(scene.Studio?.Name) ? null : scene.Studio!.Name!.Trim(),
            credits,
            dates,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            null);

        var images = new List<ImageCandidate>();
        if (!string.IsNullOrWhiteSpace(scene.Image)) {
            images.Add(new ImageCandidate("poster", scene.Image.Trim(), providerName, null, null, null, null));
        }

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
            Relationships: []);
    }

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
