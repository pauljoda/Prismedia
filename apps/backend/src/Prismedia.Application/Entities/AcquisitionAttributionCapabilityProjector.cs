using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Entities;

/// <summary>Emits accepted acquisition evidence supplied by an authorized detail-read boundary.</summary>
[EntityCapabilityProjector(205)]
internal sealed class AcquisitionAttributionCapabilityProjector : EntityCapabilityProjector<AcquisitionAttributionCapability> {
    public override AcquisitionAttributionCapability? Project(EntityCapabilityProjectionContext context) => context.AcquisitionAttribution;
}
