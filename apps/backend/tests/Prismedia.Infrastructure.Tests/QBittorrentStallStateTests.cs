using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Locks which qBittorrent states the monitor treats as a stuck download. These drive whether a transfer
/// is abandoned after the stall grace window, so the set must not drift silently.
/// </summary>
public sealed class QBittorrentStallStateTests {
    [Theory]
    [InlineData("stalledDL")]
    [InlineData("metaDL")]
    [InlineData("error")]
    [InlineData("missingFiles")]
    public void StuckStatesAreStalled(string state) =>
        Assert.True(QBittorrentProtocol.IsStalledState(state));

    [Theory]
    [InlineData("downloading")]
    [InlineData("forcedDL")]
    [InlineData("uploading")]
    [InlineData("queuedDL")]
    [InlineData("pausedDL")]
    [InlineData("checkingDL")]
    [InlineData("")]
    [InlineData(null)]
    public void HealthyOrTransientStatesAreNotStalled(string? state) =>
        Assert.False(QBittorrentProtocol.IsStalledState(state));
}
