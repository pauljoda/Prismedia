using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class MetadataFieldEvidenceTests {
    [Fact]
    public void ManualClearRemainsProtectedUntilExplicitUnlock() {
        var cleared = MetadataFieldEvidence.Unknown.WrittenByUser(true, DateTimeOffset.UtcNow);
        Assert.Equal(cleared, cleared.WrittenByProvider("provider", 0.9m, DateTimeOffset.UtcNow));
        var unlocked = cleared.WithLock(false);
        Assert.Equal(MetadataValueOrigin.User, unlocked.Origin);
        Assert.True(unlocked.IsCleared);
        var enriched = unlocked.WrittenByProvider("provider", 0.9m, DateTimeOffset.UtcNow);
        Assert.Equal(MetadataValueOrigin.Provider, enriched.Origin);
        Assert.False(enriched.IsCleared);
        Assert.Equal("provider", enriched.ProviderId);
    }
    [Fact]
    public void LockingAnExistingUnknownValueDoesNotInventItsSource() {
        var locked = MetadataFieldEvidence.Unknown.WithLock(true);
        Assert.Equal(MetadataValueOrigin.Unknown, locked.Origin);
        Assert.Null(locked.ObservedAt);
        Assert.Null(locked.ProviderId);
        Assert.Equal(locked, locked.WithLock(true));
    }
}
