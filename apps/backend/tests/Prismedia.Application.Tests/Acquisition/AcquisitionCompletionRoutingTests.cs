using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionCompletionRoutingTests {
    [Theory]
    [InlineData(EntityKind.Book, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.Movie, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.Video, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.AudioLibrary, true, JobType.AcquisitionImport)]
    [InlineData(EntityKind.AudioLibrary, false, JobType.AcquisitionImport)]
    public void RoutesSingleFileAndAlbumCompletionsToTheirOwningWorkflow(
        EntityKind kind,
        bool isUpgrade,
        JobType expected) {
        Assert.Equal(expected, AcquisitionCompletionService.CompletionJobType(kind, isUpgrade));
    }
}
