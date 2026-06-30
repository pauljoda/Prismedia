using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Searches plugin-backed metadata providers (e.g. OpenLibrary) for books at request time, returning
/// results carrying the provider id and external id so a Prismedia-direct acquisition can capture them.
/// </summary>
public interface IBookMetadataSearchSource {
    /// <summary>Book search results across enabled book-capable plugin providers. Empty when none are configured.</summary>
    Task<IReadOnlyList<RequestSearchResult>> SearchAsync(string query, bool hideNsfw, CancellationToken cancellationToken);
}

/// <summary>
/// Searches plugin-backed metadata providers for authors at request time. Author results are a container
/// kind: selecting one opens a detail that lists the author's books as toggleable children, each fanned out
/// into its own book acquisition.
/// </summary>
public interface IAuthorMetadataSearchSource {
    /// <summary>Author search results across enabled author-capable plugin providers. Empty when none are configured.</summary>
    Task<IReadOnlyList<RequestSearchResult>> SearchAuthorsAsync(string query, bool hideNsfw, CancellationToken cancellationToken);
}

/// <summary>Full metadata a plugin can resolve for a known book work-id, used to enrich a held request before import.</summary>
public sealed record BookMetadataEnrichment(string? Description, string? PosterUrl, int? Year);

/// <summary>
/// Resolves full metadata for a known provider work-id (no library entity), so a request's held metadata can
/// be enriched with the cover/description/dates the lightweight search result lacked. Reuses the plugin
/// LookupId path — no new plugin-protocol message.
/// </summary>
public interface IBookMetadataEnricher {
    /// <summary>Looks up full book metadata by provider + work-id, or null when the provider can't resolve it.</summary>
    Task<BookMetadataEnrichment?> LookupByIdAsync(string providerId, string externalId, bool hideNsfw, CancellationToken cancellationToken);
}

/// <summary>
/// Produces a provider-detail view for a plugin-sourced request, so discovery routes through a single
/// detail → select → request page. The detail reuses the plugin LookupId path with structural children, so
/// a series surfaces its volumes and an author surfaces their books as selectable child options that the
/// request fans out into one acquisition each.
/// </summary>
public interface IPluginRequestDetailSource {
    /// <summary>
    /// Builds the request detail for a provider-qualified book id (<c>"provider:itemId"</c>), or null when the
    /// provider can't resolve it. Includes series volume children when the work belongs to a series.
    /// </summary>
    Task<RequestDetailResponse?> GetBookDetailAsync(string externalId, bool hideNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Builds the request detail for a provider-qualified author id (<c>"provider:authorId"</c>), surfacing the
    /// author's books as selectable child options. Null when the provider can't resolve it.
    /// </summary>
    Task<RequestDetailResponse?> GetAuthorDetailAsync(string externalId, bool hideNsfw, CancellationToken cancellationToken);
}

/// <summary>
/// Aggregates request searches. Prismedia fulfils all requests itself through its plugin-backed acquisition
/// pipeline, so results come from plugin metadata providers (books and authors).
/// </summary>
public sealed class RequestSearchService(
    IBookMetadataSearchSource bookMetadata,
    IAuthorMetadataSearchSource authorMetadata) {
    public async Task<RequestSearchResponse> SearchAsync(RequestSearchRequest request, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.Query)) {
            return new RequestSearchResponse([], []);
        }

        var results = new List<RequestSearchResult>();
        var errors = new List<RequestProviderHealth>();

        if (request.Kinds.Count == 0 || request.Kinds.Contains(RequestMediaKind.Book)) {
            try {
                results.AddRange(await bookMetadata.SearchAsync(request.Query, request.HideNsfw, cancellationToken));
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                errors.Add(new RequestProviderHealth(Guid.Empty, RequestProviderKind.Plugin, "Book providers", ex.Message));
            }
        }

        if (request.Kinds.Count == 0 || request.Kinds.Contains(RequestMediaKind.Author)) {
            try {
                results.AddRange(await authorMetadata.SearchAuthorsAsync(request.Query, request.HideNsfw, cancellationToken));
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                errors.Add(new RequestProviderHealth(Guid.Empty, RequestProviderKind.Plugin, "Author providers", ex.Message));
            }
        }

        return new RequestSearchResponse(results, errors);
    }
}

/// <summary>Loads normalized detail metadata for a plugin-sourced request result (book or author).</summary>
public sealed class RequestDetailService(IPluginRequestDetailSource pluginDetail) {
    public async Task<RequestDetailResponse?> GetAsync(RequestProviderKind source, RequestMediaKind kind, string externalId, Guid? serviceId, bool hideNsfw, CancellationToken cancellationToken) {
        // All requests resolve through the plugin acquisition path. An author lists its books as children;
        // a book resolves itself (with series-volume children when it belongs to a series). The caller's NSFW
        // visibility is honored so a direct/bookmarked detail URL can't surface an NSFW-flagged provider's item.
        _ = source;
        _ = serviceId;
        return kind == RequestMediaKind.Author
            ? await pluginDetail.GetAuthorDetailAsync(externalId, hideNsfw, cancellationToken)
            : await pluginDetail.GetBookDetailAsync(externalId, hideNsfw, cancellationToken);
    }
}
