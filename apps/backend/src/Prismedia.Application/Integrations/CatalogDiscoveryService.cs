using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Coordinates source discovery without conflating metadata identities, source choices, and executable acquisitions.</summary>
public sealed class CatalogDiscoveryService(IntegrationConnectionAccess access, IIntegrationDiscoveryGateway gateway,
    IDiscoveryTokenProtector tokens) {
    /// <summary>Returns a bounded source page with opaque connection-bound navigation and item selections.</summary>
    public async Task<DiscoveryPageResponse> BrowseAsync(Guid connectionId, BrowseConnectionRequest request, CancellationToken cancellationToken) {
        if (!Enum.IsDefined(request.EntityKind) || request.Limit is < 1 or > 100 || request.Query?.Length > 512)
            throw new ArgumentException("Choose a valid media type, a query up to 512 characters, and a page size from 1 to 100.");
        request = request with { Query = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim() };
        var operation = request.Query is null ? IntegrationOperation.Browse : IntegrationOperation.Search;
        var authorized = await access.RequireAsync(connectionId, PluginCapability.CatalogDiscovery, operation, request.EntityKind, cancellationToken);
        string? container = null;
        if (!string.IsNullOrWhiteSpace(request.Container)) {
            var selection = tokens.ReadSelection(connectionId, request.Container);
            if (selection.EntityKind != request.EntityKind) throw new ArgumentException("The selected catalog belongs to another media type.");
            container = selection.Locator;
        }
        var cursor = string.IsNullOrWhiteSpace(request.Cursor) ? null : tokens.ReadCursor(connectionId, request, request.Cursor);
        var page = await gateway.DiscoverAsync(authorized.Manifest.Id, operation, authorized.Context,
            new(request.EntityKind, request.Query, cursor, container, request.Limit), cancellationToken);
        Validate(page, request);
        return new(connectionId, page.Title, page.Items.Select(item => new DiscoveryItemResponse(
            item.Selection.ItemId, tokens.ProtectSelection(connectionId, item.Selection), item.Selection.EntityKind,
            item.IsContainer, item.Publication, item.Offers)).ToArray(),
            string.IsNullOrEmpty(page.NextCursor) ? null : tokens.ProtectCursor(connectionId, request, page.NextCursor));
    }

    /// <summary>Resolves a protected selection for server-side acquisition. Lending, checkout, and sample offers cannot satisfy a full-content request.</summary>
    public async Task<ResolvedSourceOffer> ResolveAsync(Guid connectionId, string selectionToken, string offerId, CancellationToken cancellationToken) {
        var selection = tokens.ReadSelection(connectionId, selectionToken);
        if (string.IsNullOrWhiteSpace(offerId) || offerId.Length > 2048) throw new ArgumentException("Select a valid acquisition offer.");
        var authorized = await access.RequireAsync(connectionId, PluginCapability.AcquisitionSource,
            IntegrationOperation.Resolve, selection.EntityKind, cancellationToken);
        var resolved = await gateway.ResolveAsync(authorized.Manifest.Id, authorized.Context, new(selection, offerId), cancellationToken);
        if (resolved.Selection != selection || resolved.OfferId != offerId || resolved.Offer?.Id != offerId
            || resolved.Offer.Access != AcquisitionAccessKind.Download || resolved.Delivery is null)
            throw new IntegrationInvocationException("The source did not resolve the selected full-content offer.");
        return resolved;
    }

    private static void Validate(CatalogPage page, BrowseConnectionRequest request) {
        if (string.IsNullOrWhiteSpace(page.Title) || page.Title.Length > 512 || page.NextCursor?.Length > 8192
            || page.Items is null || page.Items.Count > request.Limit) throw InvalidPage();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in page.Items) {
            if (item?.Selection is null || item.Selection.EntityKind != request.EntityKind
                || string.IsNullOrWhiteSpace(item.Selection.ItemId) || item.Selection.ItemId.Length > 2048
                || string.IsNullOrWhiteSpace(item.Selection.Locator) || item.Selection.Locator.Length > 8192
                || !identities.Add(item.Selection.ItemId) || item.Publication is null || item.Offers is null
                || item.Offers.Count > 32 || item.IsContainer && item.Offers.Count > 0) throw InvalidPage();
            var publication = item.Publication;
            if (string.IsNullOrWhiteSpace(publication.Title) || publication.Title.Length > 512
                || publication.Description?.Length > 8192 || publication.Authors is null || publication.Authors.Count > 64
                || publication.Authors.Any(author => string.IsNullOrWhiteSpace(author) || author.Length > 256)
                || publication.ExternalIds is null || publication.ExternalIds.Count > 64
                || publication.ExternalIds.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128
                    || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 2048)
                || new[] { publication.Language, publication.Publisher, publication.EditionLabel, publication.IssueLabel }.Any(value => value?.Length > 512)) throw InvalidPage();
            var offers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var offer in item.Offers) {
                if (offer is null || string.IsNullOrWhiteSpace(offer.Id) || offer.Id.Length > 2048 || !offers.Add(offer.Id)
                    || string.IsNullOrWhiteSpace(offer.Label) || offer.Label.Length > 256 || !Enum.IsDefined(offer.Access)
                    || offer.MediaType?.Length > 256 || offer.ByteSize is < 0) throw InvalidPage();
            }
        }
    }

    private static IntegrationInvocationException InvalidPage() => new("The source returned an invalid or oversized catalog page.");
}
