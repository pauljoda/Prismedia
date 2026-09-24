using Prismedia.Domain.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

/// <summary>
/// A persisted enum member without a definition would fail only when a row reaches it, so every closed set
/// that carries behavior must define each member exactly once.
/// </summary>
public sealed class ClosedSetDefinitionTests {
    [Fact]
    public void EveryPersistedCodeHasExactlyOneDefinition() {
        AssertComplete(ManagedRequestPhaseDefinition.All.Select(definition => definition.Phase));
        AssertComplete(ManagedControlPhaseDefinition.All.Select(definition => definition.Phase));
        AssertComplete(ManagedCommandStatusDefinition.All.Select(definition => definition.Status));
        AssertComplete(IntegrationTransferPhaseDefinition.All.Select(definition => definition.Phase));
        AssertComplete(IntegrationTransferModeDefinition.All.Select(definition => definition.Mode));
        AssertComplete(RemoteJobStateDefinition.All.Select(definition => definition.State));
        AssertComplete(SourceAcquisitionStateDefinition.All.Select(definition => definition.State));
        AssertComplete(PluginCapabilityDefinition.All.Select(definition => definition.Capability));
        AssertComplete(ManagedTrackingStatusDefinition.All.Select(definition => definition.Status));
        AssertComplete(AcquisitionStatusDefinition.All.Select(definition => definition.Status));
    }

    [Fact]
    public void FailedNativeAcquisitionKeepsItsFulfillmentScope() {
        Assert.Contains(AcquisitionStatus.Failed, AcquisitionStatusDefinition.OwningFulfillment);
        Assert.DoesNotContain(AcquisitionStatus.Imported, AcquisitionStatusDefinition.OwningFulfillment);
        Assert.DoesNotContain(AcquisitionStatus.Cancelled, AcquisitionStatusDefinition.OwningFulfillment);
    }

    private static void AssertComplete<TCode>(IEnumerable<TCode> defined)
        where TCode : struct, Enum =>
        Assert.Equal(Enum.GetValues<TCode>().Order(), defined.Order());
}
