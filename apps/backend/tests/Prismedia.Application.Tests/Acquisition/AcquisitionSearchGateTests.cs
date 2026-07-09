using Prismedia.Application.Jobs.Handlers;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionSearchGateTests {
    [Theory]
    [InlineData(AcquisitionStatus.Pending)]
    [InlineData(AcquisitionStatus.Searching)]
    [InlineData(AcquisitionStatus.AwaitingSelection)]
    [InlineData(AcquisitionStatus.Failed)]
    [InlineData(AcquisitionStatus.ManualImportRequired)]
    // Cancelled is still-seeking: cancel stops that download, not the want — a monitor (or a manual
    // re-search) revives the acquisition with a fresh search.
    [InlineData(AcquisitionStatus.Cancelled)]
    public void StillSeekingStatesAreSearchable(AcquisitionStatus status) {
        Assert.True(AcquisitionSearchJobHandler.IsSearchable(status));
    }

    [Theory]
    [InlineData(AcquisitionStatus.Queued)]
    [InlineData(AcquisitionStatus.Downloading)]
    [InlineData(AcquisitionStatus.Downloaded)]
    [InlineData(AcquisitionStatus.Importing)]
    [InlineData(AcquisitionStatus.Imported)]
    public void InFlightOrSettledStatesAreNotSearchable(AcquisitionStatus status) {
        // A search here would derail a grab/import or churn a finished acquisition.
        Assert.False(AcquisitionSearchJobHandler.IsSearchable(status));
    }
}
