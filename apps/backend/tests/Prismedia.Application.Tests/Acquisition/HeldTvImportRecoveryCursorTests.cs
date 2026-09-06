using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class HeldTvImportRecoveryCursorTests {
    [Fact]
    public void RemovedAttemptsDoNotResetRecoveryToTheOldestUnresolvedItem() {
        var held = Enumerable.Range(0, 3).Select(index => new HeldTvImport(Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UnixEpoch.AddSeconds(index), null)).ToArray();
        var cursor = new HeldTvImportRecoveryCursor();
        cursor.Advance(held[1]);

        Assert.Equal([held[2], held[0]], cursor.Order([held[0], held[2]]));
    }

    [Fact]
    public void EqualTimestampsUseStableIdsBeforeWrappingToEarlierAttempts() {
        var held = Enumerable.Range(1, 3).Select(index => new HeldTvImport(Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, null)).ToArray();
        var cursor = new HeldTvImportRecoveryCursor();
        cursor.Advance(held[1]);

        Assert.Equal([held[2], held[0], held[1]], cursor.Order([held[2], held[1], held[0]]));
    }
}
