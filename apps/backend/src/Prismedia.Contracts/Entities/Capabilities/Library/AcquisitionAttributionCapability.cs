using Prismedia.Contracts.Integrations;

namespace Prismedia.Contracts.Entities;

/// <summary>Source statements accepted for an acquisition whose verified file was imported into this entity.</summary>
/// <param name="OperationId">Stable acquisition identity, used to distinguish separately accepted source snapshots.</param>
/// <param name="AcceptedAt">When Prismedia accepted the source snapshot, not the date the upstream work was created.</param>
/// <param name="Attribution">Source-supplied credit and license statements; these do not replace curated entity metadata.</param>
public sealed record EntityAcquisitionAttribution(Guid OperationId, DateTimeOffset AcceptedAt, CatalogAttribution Attribution);

/// <summary>Retained source attribution for up to 100 recent completed imports of this exact entity.</summary>
/// <param name="Items">Immutable accepted statements, newest first. No connection secrets or delivery locators are exposed.</param>
/// <param name="Unavailable">At least one matching saved snapshot could not be read; the library entity remains accessible.</param>
[CapabilityKind("acquisition-attribution")]
public sealed record AcquisitionAttributionCapability(IReadOnlyList<EntityAcquisitionAttribution> Items, bool Unavailable = false) : EntityCapability;
