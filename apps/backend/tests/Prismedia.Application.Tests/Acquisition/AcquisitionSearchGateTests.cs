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
    [InlineData(AcquisitionStatus.Cancelled)]
    public void StillSeekingStatesCanScheduleSearch(AcquisitionStatus status) {
        Assert.True(AcquisitionSearchJobHandler.CanScheduleSearch(status));
    }

    [Theory]
    [InlineData(AcquisitionStatus.Queued)]
    [InlineData(AcquisitionStatus.Downloading)]
    [InlineData(AcquisitionStatus.Downloaded)]
    [InlineData(AcquisitionStatus.Importing)]
    [InlineData(AcquisitionStatus.Imported)]
    [InlineData(AcquisitionStatus.Stopping)]
    public void InFlightOrSettledStatesAreNotSearchable(AcquisitionStatus status) {
        // A search here would derail a grab/import or churn a finished acquisition.
        Assert.False(AcquisitionSearchJobHandler.CanScheduleSearch(status));
    }
}
