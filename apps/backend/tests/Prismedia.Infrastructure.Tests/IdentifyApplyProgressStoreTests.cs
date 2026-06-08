using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyApplyProgressStoreTests {
    [Fact]
    public void ReportStepAdvancesCurrentEntityAndPath() {
        var store = new InMemoryIdentifyApplyProgressStore();
        var operationId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        store.Begin(operationId, entityId, total: 4);
        var snapshot = store.ReportStep(
            operationId,
            new IdentifyApplyProgressStep(EntityKind.Video, "Episode 3", ["Series", "Season 1", "Episode 3"]));

        Assert.Equal("running", snapshot.State);
        Assert.Equal(entityId, snapshot.EntityId);
        Assert.Equal(1, snapshot.CurrentIndex);
        Assert.Equal(4, snapshot.Total);
        Assert.Equal("Episode 3", snapshot.CurrentTitle);
        Assert.Equal(["Series", "Season 1", "Episode 3"], snapshot.CurrentPath);
    }

    [Fact]
    public void CompleteMovesProgressToTotal() {
        var store = new InMemoryIdentifyApplyProgressStore();
        var operationId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        store.Begin(operationId, entityId, total: 3);
        store.ReportStep(operationId, new IdentifyApplyProgressStep(EntityKind.VideoSeries, "Series", ["Series"]));
        var snapshot = store.Complete(operationId);

        Assert.Equal("succeeded", snapshot.State);
        Assert.Equal(3, snapshot.CurrentIndex);
        Assert.Null(snapshot.Error);
    }
}
