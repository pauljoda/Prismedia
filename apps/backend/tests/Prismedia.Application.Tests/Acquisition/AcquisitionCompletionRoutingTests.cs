using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionCompletionRoutingTests {
    [Theory]
    [InlineData(EntityKind.Book, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.Movie, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.Video, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.VideoEpisode, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.VideoSeason, true, JobType.AcquisitionImport)]
    [InlineData(EntityKind.AudioLibrary, true, JobType.AcquisitionImport)]
    [InlineData(EntityKind.AudioTrack, true, JobType.AcquisitionImport)]
    [InlineData(EntityKind.AudioLibrary, false, JobType.AcquisitionImport)]
    public void RoutesSingleFileAndAlbumCompletionsToTheirOwningWorkflow(
        EntityKind kind,
        bool isUpgrade,
        JobType expected) {
        Assert.Equal(expected, AcquisitionCompletionService.CompletionJobType(kind, isUpgrade));
    }

    [Fact]
    public void EveryRequestAcquisitionKindHasDefinitionOwnedCompletionRouting() {
        var acquisitionKinds = RequestKindRegistry.All
            .Select(descriptor => descriptor.AcquisitionKind)
            .Distinct()
            .Order()
            .ToArray();

        foreach (var kind in acquisitionKinds) {
            var definition = EntityKindRegistry.Describe(kind);
            var expected = definition.UpgradeMode == EntityUpgradeMode.Import
                ? JobType.AcquisitionImport
                : JobType.AcquisitionUpgradeReplace;
            Assert.Equal(expected, AcquisitionCompletionService.CompletionJobType(kind, isUpgrade: true));
        }
    }
}
