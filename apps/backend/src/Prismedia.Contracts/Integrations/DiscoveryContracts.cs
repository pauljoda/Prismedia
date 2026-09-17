using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Browser discovery request. Cursor and container tokens are opaque host-protected values from earlier responses.</summary>
public sealed record BrowseConnectionRequest(EntityKind EntityKind, string? Query = null, string? Cursor = null, string? Container = null, int Limit = 25);

/// <summary>Plugin discovery input. Source-owned cursors and locators are decoded only after host authorization.</summary>
public sealed record IntegrationDiscoveryInput(EntityKind EntityKind, string? Query, string? Cursor, string? Container, int Limit);

/// <summary>Exact source selection scoped by its connection, with an opaque provider locator independent of stable item identity.</summary>
public sealed record SourceSelection(string ItemId, string Locator, EntityKind EntityKind);

/// <summary>A source's acquisition choice. IDs do not contain credentials or executable download URLs.</summary>
public sealed record CatalogOffer(string Id, string Label, AcquisitionAccessKind Access, string? MediaType = null, long? ByteSize = null);

/// <summary>Source metadata describing a particular holding or edition. It is not automatically applied to the library.</summary>
public sealed record CatalogPublication(
    string Title, string? Description, IReadOnlyList<string> Authors, IReadOnlyDictionary<string, string> ExternalIds,
    string? Language = null, string? Publisher = null, string? EditionLabel = null, string? IssueLabel = null,
    CatalogAttribution? Attribution = null);

/// <summary>Plain-text attribution and licensing statements reported by a source, retained with accepted import intent. These statements do not alter curated library metadata.</summary>
public sealed record CatalogAttribution(string SourceUrl, string? Creator, string? Credit, string? LicenseName,
    string? LicenseUrl, string? UsageTerms, bool? AttributionRequired);

/// <summary>Internal plugin result retaining the provider locator. Containers represent navigation, never downloadable content.</summary>
public sealed record CatalogItem(SourceSelection Selection, bool IsContainer, CatalogPublication Publication, IReadOnlyList<CatalogOffer> Offers);

/// <summary>A bounded page returned by an integration plugin, with a provider-owned continuation cursor.</summary>
public sealed record CatalogPage(string Title, IReadOnlyList<CatalogItem> Items, string? NextCursor = null);

/// <summary>Public source item with a host-protected selection token in place of the provider locator.</summary>
public sealed record DiscoveryItemResponse(string Id, string SelectionToken, EntityKind EntityKind, bool IsContainer,
    CatalogPublication Publication, IReadOnlyList<CatalogOffer> Offers);

/// <summary>Public page scoped to one connection. NextCursor can only be used with that connection and media kind.</summary>
public sealed record DiscoveryPageResponse(Guid ConnectionId, string Title, IReadOnlyList<DiscoveryItemResponse> Items, string? NextCursor);

/// <summary>Resolves exactly one acquisition offer selected from a source item.</summary>
public sealed record ResolveSourceOfferInput(SourceSelection Selection, string OfferId);

/// <summary>Server-only byte delivery instructions. Credentials and signed URLs never become browser-visible offers.</summary>
public sealed record HttpArtifactDelivery(string Url, IReadOnlyDictionary<string, string> Headers,
    string SuggestedFileName, long? ByteSize = null, string? Sha256 = null, DateTimeOffset? ExpiresAt = null,
    string? Sha1 = null);

/// <summary>A resolved full-content offer. The host still owns validation, staging, verification, and import.</summary>
public sealed record ResolvedSourceOffer(SourceSelection Selection, string OfferId, CatalogPublication Publication,
    CatalogOffer Offer, HttpArtifactDelivery Delivery);
