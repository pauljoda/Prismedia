using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionSearchGateTests {
    [Fact]
    public void SearchSummaryDistinguishesFailedAndQueryLimitedSources() {
        var outcome = new AcquisitionSearchOutcome([], [
            new IndexerSearchError(Guid.NewGuid(), "Unreachable", "connection refused"),
            new IndexerSearchError(Guid.NewGuid(), "Limited", "hourly query limit", WasSkipped: true)
        ]);

        Assert.Equal(
            "0 acceptable of 0 release(s). 1 indexer(s) failed: Unreachable. 1 indexer(s) skipped by their query limits: Limited.",
            AcquisitionSearchJobHandler.BuildMessage(outcome));
    }

    [Theory]
    [InlineData(AcquisitionStatus.Pending)]
    [InlineData(AcquisitionStatus.WaitingForRelease)]
    [InlineData(AcquisitionStatus.ManualSearchRequired)]
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
    [InlineData(AcquisitionStatus.WaitingForDownloadClient)]
    [InlineData(AcquisitionStatus.Downloaded)]
    [InlineData(AcquisitionStatus.Importing)]
    [InlineData(AcquisitionStatus.Imported)]
    [InlineData(AcquisitionStatus.Stopping)]
    public void InFlightOrSettledStatesAreNotSearchable(AcquisitionStatus status) {
        // A search here would derail a grab/import or churn a finished acquisition.
        Assert.False(AcquisitionSearchJobHandler.CanScheduleSearch(status));
    }
}
