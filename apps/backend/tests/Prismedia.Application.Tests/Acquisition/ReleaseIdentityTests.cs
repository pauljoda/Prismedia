using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ReleaseIdentityTests {
    [Fact]
    public void InfoHashIsAuthoritativeAndCaseInsensitive() {
        var upper = ReleaseIdentity.For("ABC123", "Indexer A", "Some Title");
        var lower = ReleaseIdentity.For("abc123", "Indexer B", "A Completely Different Title");

        // Same torrent (same hash) is the same identity regardless of indexer or title casing.
        Assert.Equal(upper, lower);
        Assert.StartsWith("hash:", upper);
    }

    [Fact]
    public void FallsBackToIndexerAndNormalizedTitleWhenNoHash() {
        var a = ReleaseIdentity.For(null, "MyIndexer", "Some   Book  (EPUB)");
        var b = ReleaseIdentity.For("  ", "myindexer", "some book (epub)");

        // Whitespace runs collapse and casing is ignored, so these match.
        Assert.Equal(a, b);
        Assert.StartsWith("title:", a);
    }

    [Fact]
    public void DifferentTitlesProduceDifferentIdentities() {
        var a = ReleaseIdentity.For(null, "MyIndexer", "Book One");
        var b = ReleaseIdentity.For(null, "MyIndexer", "Book Two");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentIndexersProduceDifferentTitleIdentities() {
        var a = ReleaseIdentity.For(null, "IndexerA", "Same Book");
        var b = ReleaseIdentity.For(null, "IndexerB", "Same Book");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashAndTitleIdentitiesNeverCollide() {
        // The prefixes keep a title that looks like a hash from colliding with a real hash.
        var hash = ReleaseIdentity.For("deadbeef", null, "deadbeef");
        var title = ReleaseIdentity.For(null, "x", "deadbeef");

        Assert.NotEqual(hash, title);
    }
}
