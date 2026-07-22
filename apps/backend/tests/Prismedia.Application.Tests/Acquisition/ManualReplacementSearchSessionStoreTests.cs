using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ManualReplacementSearchSessionStoreTests {
    [Fact]
    public async Task SessionRemainsAvailableAfterAQueueAttemptSoTheDurableHandoffCanBeReplayed() {
        var store = new ManualReplacementSearchSessionStore();
        var parentId = Guid.NewGuid();
        var session = store.Create(parentId, [Candidate()]);

        var first = await store.ExecuteExclusiveAsync(
            session.Id,
            parentId,
            value => Task.FromResult(value));
        var replay = await store.ExecuteExclusiveAsync(
            session.Id,
            parentId,
            value => Task.FromResult(value));

        Assert.NotNull(first);
        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task SessionCannotBeUsedForAnotherEntity() {
        var store = new ManualReplacementSearchSessionStore();
        var session = store.Create(Guid.NewGuid(), [Candidate()]);

        Assert.Null(await store.ExecuteExclusiveAsync(
            session.Id,
            Guid.NewGuid(),
            value => Task.FromResult(value)));
    }

    private static ReviewedReleaseCandidate Candidate() => new(
        Guid.NewGuid(),
        new ScoredRelease(
            new IndexerRelease("Book retail epub", 1_000, 5, 1, DownloadProtocol.Torrent, "https://download", null, "hash", null, null, null),
            null,
            "Indexer",
            true,
            1,
            []));
}
