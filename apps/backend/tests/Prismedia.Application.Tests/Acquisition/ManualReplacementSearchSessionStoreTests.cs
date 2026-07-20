using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ManualReplacementSearchSessionStoreTests {
    [Fact]
    public void ClaimedSessionCanOnlyMaterializeOnce() {
        var store = new ManualReplacementSearchSessionStore();
        var parentId = Guid.NewGuid();
        var session = store.Create(parentId, [Candidate()]);

        var claimed = store.Claim(session.Id, parentId);
        var replay = store.Claim(session.Id, parentId);

        Assert.NotNull(claimed);
        Assert.Null(replay);
    }

    [Fact]
    public void SessionCannotBeClaimedForAnotherAcquisition() {
        var store = new ManualReplacementSearchSessionStore();
        var session = store.Create(Guid.NewGuid(), [Candidate()]);

        Assert.Null(store.Claim(session.Id, Guid.NewGuid()));
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
