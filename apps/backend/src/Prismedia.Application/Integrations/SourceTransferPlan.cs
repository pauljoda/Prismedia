using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Durable direct or prepared source selection. Transient delivery URLs and credentials are resolved immediately
/// before transfer.</summary>
public sealed record SourceTransferPlan(SourceSelection Selection, string OfferId, CatalogPublication? Publication = null,
    CatalogOffer? Offer = null);
