using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Runs one schema-driven request search through an explicitly selected plugin.</summary>
public interface IPluginRequestSearchSource {
    /// <summary>Searches one plugin without falling through to other providers.</summary>
    Task<IReadOnlyList<RequestSearchResult>> SearchAsync(
        RequestKindDescriptor descriptor,
        string pluginId,
        IReadOnlyDictionary<string, string> fields,
        bool hideNsfw,
        CancellationToken cancellationToken);
}

/// <summary>An expected validation failure in a schema-driven plugin request search.</summary>
/// <param name="message">Human-readable reason the selected plugin search cannot run.</param>
public sealed class RequestSearchValidationException(string message) : ArgumentException(message);

/// <summary>Full metadata a plugin can resolve for a persistent external identity, used to enrich a held request before import.</summary>
public sealed record RequestMetadataEnrichment(string? Description, string? PosterUrl, int? Year);

/// <summary>
/// Resolves full metadata for a known external identity (no library entity), so a request's held metadata
/// can be enriched with the cover/description/dates the lightweight search result lacked. Reuses the
/// plugin LookupId path — no new plugin-protocol message.
/// </summary>
public interface IRequestMetadataEnricher {
    /// <summary>Looks up full metadata by media kind and persistent identity, or null when no plugin can resolve it.</summary>
    Task<RequestMetadataEnrichment?> LookupByIdAsync(
        EntityKind kind,
        ExternalIdentity identity,
        bool hideNsfw,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves a complete request-review proposal through the exact plugin selected during search.
/// </summary>
public interface IPluginRequestReviewSource {
    /// <summary>
    /// Returns a canonical, unflattened review or null when the selected plugin is disabled, gated,
    /// does not declare the identity for the requested kind, or cannot resolve the item.
    /// </summary>
    Task<RequestReviewResponse?> ReviewAsync(
        RequestReviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-resolves the review through the exact selected plugin without using a proposal cache. Commit
    /// uses this path so the reviewed revision is compared with current upstream content.
    /// </summary>
    Task<RequestReviewResponse?> RevalidateAsync(
        RequestReviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken);
}

/// <summary>An expected reviewed-commit validation failure that maps to request_invalid.</summary>
public sealed class RequestCommitValidationException(string message) : ArgumentException(message);

/// <summary>Raised when the proposal changed after the user reviewed it and before commit.</summary>
public sealed class RequestProposalChangedException()
    : InvalidOperationException("The request details changed after review. Review the updated proposal before requesting it.");

/// <summary>Coordinates one canonical schema-driven Discover search.</summary>
public sealed class RequestPluginSearchService(IPluginRequestSearchSource source) {
    /// <summary>Validates the core request shape and searches only the selected plugin.</summary>
    public async Task<RequestSearchResponse> SearchAsync(
        RequestPluginSearchRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var descriptor = RequestKindRegistry.Find(request.Kind);
        if (descriptor is null || !descriptor.Discoverable) {
            throw new RequestSearchValidationException("The selected kind is not available in Discover search.");
        }
        if (string.IsNullOrWhiteSpace(request.PluginId)) {
            throw new RequestSearchValidationException("A plugin id is required for Discover search.");
        }
        if (request.Fields is null) {
            throw new RequestSearchValidationException("Plugin search fields are required.");
        }

        try {
            var results = await source.SearchAsync(
                descriptor,
                request.PluginId,
                request.Fields,
                hideNsfw,
                cancellationToken);
            return new RequestSearchResponse(results, []);
        } catch (RequestSearchValidationException) {
            throw;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new RequestSearchResponse(
                [],
                [new RequestProviderHealth(Guid.Empty, RequestProviderKind.Plugin, request.PluginId, ex.Message)]);
        }
    }
}
