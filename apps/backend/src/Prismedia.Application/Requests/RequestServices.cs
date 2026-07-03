using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Searches plugin-backed metadata providers for one requestable kind at request time, returning
/// results carrying the provider id and external id so a Prismedia-direct acquisition can capture them.
/// Kind behavior (which plugin kind to query, container gating) comes from the descriptor.
/// </summary>
public interface IRequestMetadataSearchSource {
    /// <summary>Search results for one request kind across enabled capable plugin providers. Empty when none are configured.</summary>
    Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestKindDescriptor descriptor, string query, bool hideNsfw, CancellationToken cancellationToken);
}

/// <summary>Full metadata a plugin can resolve for a known provider work-id, used to enrich a held request before import.</summary>
public sealed record RequestMetadataEnrichment(string? Description, string? PosterUrl, int? Year);

/// <summary>
/// Resolves full metadata for a known provider work-id (no library entity), so a request's held metadata
/// can be enriched with the cover/description/dates the lightweight search result lacked. Reuses the
/// plugin LookupId path — no new plugin-protocol message.
/// </summary>
public interface IRequestMetadataEnricher {
    /// <summary>Looks up full metadata by media kind + provider + work-id, or null when the provider can't resolve it.</summary>
    Task<RequestMetadataEnrichment?> LookupByIdAsync(EntityKind kind, string providerId, string externalId, bool hideNsfw, CancellationToken cancellationToken);
}

/// <summary>
/// Produces a provider-detail view for a plugin-sourced request, so discovery routes through a single
/// detail → select → request page. The detail reuses the plugin LookupId path with structural children,
/// so a container surfaces its works (an author's books, an artist's albums) as selectable child options.
/// </summary>
public interface IPluginRequestDetailSource {
    /// <summary>
    /// Builds the request detail for a provider-qualified id (<c>"provider:itemId"</c>) of the given
    /// kind, or null when the provider can't resolve it.
    /// </summary>
    Task<RequestDetailResponse?> GetDetailAsync(RequestKindDescriptor descriptor, string externalId, bool hideNsfw, CancellationToken cancellationToken);
}

/// <summary>
/// Aggregates request searches across the requestable kinds. Prismedia fulfils all requests itself
/// through its plugin-backed acquisition pipeline, so results come from plugin metadata providers.
/// </summary>
public sealed class RequestSearchService(IRequestMetadataSearchSource source) {
    public async Task<RequestSearchResponse> SearchAsync(RequestSearchRequest request, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.Query)) {
            return new RequestSearchResponse([], []);
        }

        // Unit kinds (seasons, episodes) exist only inside their parent's flow — an "all kinds"
        // search never queries them, and asking for one explicitly is refused the same way.
        var kinds = (request.Kinds.Count == 0
            ? RequestKindRegistry.All
            : request.Kinds.Select(RequestKindRegistry.Find).Where(d => d is not null).Select(d => d!).ToArray())
            .Where(descriptor => descriptor.Discoverable)
            .ToArray();

        var results = new List<RequestSearchResult>();
        var errors = new List<RequestProviderHealth>();
        foreach (var descriptor in kinds) {
            try {
                results.AddRange(await source.SearchAsync(descriptor, request.Query, request.HideNsfw, cancellationToken));
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                errors.Add(new RequestProviderHealth(
                    Guid.Empty, RequestProviderKind.Plugin, $"{descriptor.Kind.ToCode()} providers", ex.Message));
            }
        }

        return new RequestSearchResponse(results, errors);
    }
}

/// <summary>Loads normalized detail metadata for a plugin-sourced request result of any requestable kind.</summary>
public sealed class RequestDetailService(IPluginRequestDetailSource pluginDetail) {
    public async Task<RequestDetailResponse?> GetAsync(RequestProviderKind source, RequestMediaKind kind, string externalId, Guid? serviceId, bool hideNsfw, CancellationToken cancellationToken) {
        // All requests resolve through the plugin acquisition path; per-kind behavior comes from the
        // registry. The caller's NSFW visibility is honored so a direct/bookmarked detail URL can't
        // surface an NSFW-flagged provider's item.
        _ = source;
        _ = serviceId;
        var descriptor = RequestKindRegistry.Find(kind);
        return descriptor is null
            ? null
            : await pluginDetail.GetDetailAsync(descriptor, externalId, hideNsfw, cancellationToken);
    }
}
