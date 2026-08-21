using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class SerializedComicEntityKindTests {
    [Fact]
    public void SerializedComicHierarchyUsesTheSharedOrderedSequenceTopology() {
        var series = EntityKindRegistry.Describe(EntityKind.ComicSeries);
        var volume = EntityKindRegistry.Describe(EntityKind.ComicVolume);
        var installment = EntityKindRegistry.Describe(EntityKind.ComicInstallment);

        Assert.Equal(
            new EntityProgressTopology.OrderedContainerTopology(EntityKind.ComicInstallment),
            series.ProgressTopology);
        Assert.Equal(
            new EntityProgressTopology.OrderedContainerTopology(EntityKind.ComicInstallment),
            volume.ProgressTopology);

        var rollup = Assert.IsType<EntityProgressTopology.OrderedRollupTopology>(installment.ProgressTopology);
        Assert.Equal(EntityKind.ComicInstallment, rollup.ItemKind);
        Assert.Equal([EntityKind.ComicVolume, EntityKind.ComicSeries], rollup.ContainerKinds);

        Assert.Equal(EntityStructurePolicy.RootOnly, series.StructurePolicy);
        Assert.Equal([EntityKind.ComicSeries], volume.StructurePolicy.AllowedParentKinds);
        Assert.Equal(
            [EntityKind.ComicSeries, EntityKind.ComicVolume],
            installment.StructurePolicy.AllowedParentKinds);
        Assert.DoesNotContain(EntityKind.Book, volume.StructurePolicy.AllowedParentKinds);
        Assert.DoesNotContain(EntityKind.Book, installment.StructurePolicy.AllowedParentKinds);
    }

    [Fact]
    public void ComicInstallmentKeepsItsReleasedWorkSubtypeAndReadingState() {
        var installment = new ComicInstallment(
            Guid.NewGuid(),
            "Chapter 10.5",
            ComicInstallmentKind.Chapter,
            parentEntityId: Guid.NewGuid(),
            sortOrder: 11);

        Assert.Equal(ComicInstallmentKind.Chapter, installment.InstallmentKind);
        Assert.Equal(11, installment.SortOrder);
        Assert.True(installment.HasCapability<CapabilityProgress>());
        Assert.True(installment.HasCapability<CapabilityConsumption>());
    }

    [Fact]
    public void SerializedComicDefinitionsExposeSeriesMetadataAndInstallmentSubtype() {
        var series = new ComicSeries(Guid.NewGuid(), "Witch Hat Atelier", "releasing");
        var installment = new ComicInstallment(
            Guid.NewGuid(),
            "Chapter 83",
            ComicInstallmentKind.Chapter,
            parentEntityId: series.Id);

        var seriesCapability = Assert.IsType<Prismedia.Contracts.Entities.SeriesMetadataCapability>(
            Assert.Single(series.Definition.ProjectCapabilities(
                series,
                new EntityKindProjectionContext(CurrentUserId: null))));
        var installmentCapability = Assert.IsType<Prismedia.Contracts.Entities.ComicInstallmentMetadataCapability>(
            Assert.Single(installment.Definition.ProjectCapabilities(
                installment,
                new EntityKindProjectionContext(CurrentUserId: null))));

        Assert.Equal("releasing", seriesCapability.Status);
        Assert.Equal(ComicInstallmentKind.Chapter, installmentCapability.InstallmentKind);
    }

    [Fact]
    public void SerializedComicRequestsAllowCollectedAndDirectInstallmentChildren() {
        var seriesRequest = Assert.Single(EntityKindRegistry.Describe(EntityKind.ComicSeries).RequestKinds);
        var volumeRequest = Assert.Single(EntityKindRegistry.Describe(EntityKind.ComicVolume).RequestKinds);
        var installmentRequest = Assert.Single(EntityKindRegistry.Describe(EntityKind.ComicInstallment).RequestKinds);

        Assert.Equal(RequestMediaKind.ComicSeries, seriesRequest.Kind);
        Assert.Equal(
            [RequestMediaKind.ComicVolume, RequestMediaKind.ComicInstallment],
            seriesRequest.ChildKinds);
        Assert.Equal(RequestReviewSelection.DirectChildren, seriesRequest.ReviewSelection);
        Assert.Equal(EntityKind.ComicVolume, seriesRequest.AcquisitionKind);

        Assert.Equal(RequestMediaKind.ComicVolume, volumeRequest.Kind);
        Assert.Equal([RequestMediaKind.ComicInstallment], volumeRequest.ChildKinds);
        Assert.True(volumeRequest.MaterializeChildPhantoms);
        Assert.True(volumeRequest.AcquireFromEntity);
        Assert.True(EntityKindRegistry.Describe(EntityKind.ComicVolume).IsFulfilledBySourceBackedSubtree);

        Assert.Equal(RequestMediaKind.ComicInstallment, installmentRequest.Kind);
        Assert.Empty(installmentRequest.ChildKinds);
        Assert.True(installmentRequest.AcquireFromEntity);
        Assert.Equal(1, EntityKindRegistry.Describe(EntityKind.ComicInstallment).AutomaticImportFileLimit);
        Assert.Null(EntityKindRegistry.Describe(EntityKind.Book).AutomaticImportFileLimit);
    }

    [Fact]
    public void SerializedComicAcquisitionProfileUsesArchivePlacementAndComicScanning() {
        var profile = EntityKindRegistry.Describe(EntityKind.ComicSeries).AcquisitionProfile;

        Assert.NotNull(profile);
        Assert.Equal(AcquisitionNamingFamily.Book, profile.NamingFamily);
        Assert.Equal(AcquisitionCheckpointProtocol.Placement, profile.CheckpointProtocol);
        Assert.Equal(JobType.ScanComic, profile.ImportScanJobType);
        Assert.Contains(EntityDateType.Publication, profile.SupportedReleaseDateTypes);
        Assert.Equal("{Series}/{VolumeFolder}/{Title}.{ext}", profile.DefaultNamingTemplate);
    }
}
