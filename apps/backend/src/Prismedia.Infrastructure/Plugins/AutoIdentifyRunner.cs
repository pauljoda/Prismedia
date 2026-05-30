using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Plugins;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Runs auto identify for a single scanned entity: it walks the user's ordered provider list and
/// applies the first proposal that clears the configured confidence bar (or is an exact match),
/// applying the full proposal — scalar fields, structural children, relationships, and artwork —
/// exactly as a manual identify-and-apply would. Successfully identified entities are marked
/// organized so the un-organized-only gate skips them on later scans.
/// </summary>
public sealed class AutoIdentifyRunner(
    SettingsService settings,
    IIdentifyProviderService identify,
    PrismediaDbContext db,
    ILogger<AutoIdentifyRunner> logger) : IAutoIdentifyRunner {
    /// <summary>
    /// Maps stored entity kind codes to the high-level selector kinds exposed in settings.
    /// Kinds absent from this map (structural seasons, taxonomy entities, etc.) are never auto-identified.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SelectorKindByEntityKind =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["video"] = "video",
            ["gallery"] = "gallery",
            ["image"] = "image",
            ["audio-track"] = "audio",
            ["book"] = "book",
        };

    public async Task<AutoIdentifyResult> RunAsync(Guid entityId, CancellationToken cancellationToken) {
        var config = await settings.GetAutoIdentifySettingsAsync(cancellationToken);
        if (!config.Enabled) {
            return new AutoIdentifyResult(false, SkipReason: "auto identify disabled");
        }

        if (config.Providers.Count == 0) {
            return new AutoIdentifyResult(false, SkipReason: "no providers configured");
        }

        var entity = await db.Entities
            .FirstOrDefaultAsync(row => row.Id == entityId && row.DeletedAt == null, cancellationToken);
        if (entity is null) {
            return new AutoIdentifyResult(false, SkipReason: "entity not found");
        }

        // Only identify top-level entities. A child (an episode in a series, an image in a gallery,
        // a track in an album) is filled by cascading from its identified parent, so identifying it
        // directly would duplicate and conflict with the parent's work.
        if (entity.ParentEntityId is not null) {
            return new AutoIdentifyResult(false, SkipReason: "child entity; its parent is identified instead");
        }

        if (!SelectorKindByEntityKind.TryGetValue(entity.KindCode, out var selectorKind)) {
            return new AutoIdentifyResult(false, SkipReason: $"kind '{entity.KindCode}' is not auto-identifiable");
        }

        var selectedKinds = config.EntityKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!selectedKinds.Contains(selectorKind)) {
            return new AutoIdentifyResult(false, SkipReason: $"kind '{selectorKind}' not selected");
        }

        if (config.UnorganizedOnly && entity.IsOrganized) {
            return new AutoIdentifyResult(false, SkipReason: "already organized");
        }

        // Restrict to user-selected providers that are installed, enabled, and capable of this kind,
        // preserving the user's configured priority order.
        var capable = (await identify.ListProvidersAsync(selectorKind, cancellationToken))
            .Where(provider => provider.Installed && provider.Enabled)
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var providerId in config.Providers) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!capable.Contains(providerId)) {
                continue;
            }

            IdentifyPluginResponse response;
            try {
                response = await identify.IdentifyAsync(entityId, providerId, query: null, hideNsfw: false, cancellationToken);
            } catch (Exception ex) {
                logger.LogWarning(ex, "AutoIdentify: provider {Provider} failed for entity {EntityId}", providerId, entityId);
                continue;
            }

            if (!response.Ok || response.Result is null) {
                continue;
            }

            var proposal = response.Result;
            if (!MeetsConfidenceBar(proposal, config.ConfidenceThreshold)) {
                continue;
            }

            var fields = SelectAllPresentFields(proposal);
            var images = SelectDefaultImages(proposal);
            var applied = await identify.ApplyAsync(entityId, proposal, fields, images, cancellationToken);
            if (!applied) {
                continue;
            }

            await MarkOrganizedAsync(entityId, cancellationToken);
            return new AutoIdentifyResult(true, providerId, proposal.Confidence);
        }

        return new AutoIdentifyResult(false, SkipReason: "no confident match");
    }

    /// <summary>
    /// A proposal qualifies for auto-apply when it carries concrete metadata and either reports a
    /// confidence at or above the threshold or reports no confidence at all (treated as an exact /
    /// definitive match, as with id/url lookups and deterministic scrapers).
    /// </summary>
    private static bool MeetsConfidenceBar(EntityMetadataProposal proposal, double threshold) {
        var patch = proposal.Patch;
        var hasConcreteMetadata = !string.IsNullOrWhiteSpace(patch.Title) || patch.ExternalIds.Count > 0;
        if (!hasConcreteMetadata) {
            return false;
        }

        if (proposal.Confidence is not { } confidence) {
            return true;
        }

        // Confidence is a 0–1 fraction by contract; tolerate providers that report a 0–100 percentage.
        var normalized = (double)confidence;
        if (normalized > 1d) {
            normalized /= 100d;
        }

        return normalized >= threshold;
    }

    /// <summary>
    /// Builds the full set of field keys present in the proposal so the apply behaves like a manual
    /// "select all" — scalar fields, relationships (credits/studio/tags), and artwork. Structural
    /// children are always applied by the apply service regardless of the selected field set.
    /// </summary>
    private static IReadOnlyCollection<string> SelectAllPresentFields(EntityMetadataProposal proposal) {
        var patch = proposal.Patch;
        var fields = new List<string>();

        if (!string.IsNullOrWhiteSpace(patch.Title)) fields.Add("title");
        if (!string.IsNullOrWhiteSpace(patch.Description)) fields.Add("description");
        if (patch.ExternalIds.Count > 0) fields.Add("externalIds");
        if (patch.Urls.Count > 0) fields.Add("urls");
        if (patch.Dates.Count > 0) fields.Add("dates");
        if (patch.Stats.Count > 0) fields.Add("stats");
        if (patch.Positions.Count > 0) fields.Add("positions");
        if (!string.IsNullOrWhiteSpace(patch.Classification)) fields.Add("classification");
        if (patch.Rating.HasValue) fields.Add("rating");
        if (patch.Flags is not null) fields.Add("flags");
        if (patch.Tags.Count > 0) fields.Add("tags");
        if (!string.IsNullOrWhiteSpace(patch.Studio)) fields.Add("studio");
        if (patch.Credits.Count > 0) fields.Add("credits");
        if (proposal.Images.Count > 0) fields.Add("images");

        return fields;
    }

    /// <summary>
    /// Picks the first candidate per artwork role, mirroring the manual review's default image
    /// selection. Logo art is skipped because it is only meaningful for studios, which are applied
    /// as relationship proposals with their own artwork handling.
    /// </summary>
    private static IReadOnlyDictionary<string, string?>? SelectDefaultImages(EntityMetadataProposal proposal) {
        if (proposal.Images.Count == 0) {
            return null;
        }

        var images = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var image in proposal.Images) {
            if (string.IsNullOrWhiteSpace(image.Url) || string.Equals(image.Kind, "logo", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            images.TryAdd(image.Kind, image.Url);
        }

        return images.Count > 0 ? images : null;
    }

    private async Task MarkOrganizedAsync(Guid entityId, CancellationToken cancellationToken) {
        var entity = await db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null || entity.IsOrganized) {
            return;
        }

        entity.IsOrganized = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
