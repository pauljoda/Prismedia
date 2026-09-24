using Prismedia.Application.Integrations;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>
/// Network-free people enrichment inputs for one connected holding: its local metadata root, pinned identity,
/// configuration fingerprint, optional manager connection, and exact metadata routes.
/// </summary>
internal sealed record ExternalPeopleEnrichmentPlan(
    Guid HoldingId,
    Guid EntityId,
    EntityKind EntityKind,
    ManagedItemInput Item,
    string Fingerprint,
    AuthorizedIntegrationConnection? Manager,
    IReadOnlyList<PluginIdentityRoute> MetadataRoutes);
