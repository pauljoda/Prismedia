using Microsoft.AspNetCore.Http;

namespace Prismedia.Api.Tests;

public sealed class SpaDevProxyTests {
    [Theory]
    [InlineData("/api/entities", true)]
    [InlineData("/assets/videos/1/thumb.jpg", true)]
    [InlineData("/Videos/0d42e2e4-b181-4392-aae8-3c2184422a97/stream", true)]
    [InlineData("/Videos/ActiveEncodings", true)]
    [InlineData("/Library/VirtualFolders", true)]
    [InlineData("/library", false)]
    [InlineData("/videos", false)]
    [InlineData("/videos/0d42e2e4-b181-4392-aae8-3c2184422a97", false)]
    // Lowercase Jellyfin sub-resources (e.g. Infuse direct play) must still reach the backend.
    [InlineData("/videos/0d42e2e4-b181-4392-aae8-3c2184422a97/stream", true)]
    [InlineData("/videos/0d42e2e4-b181-4392-aae8-3c2184422a97/master.m3u8", true)]
    [InlineData("/videos/ActiveEncodings", true)]
    // Lowercase SPA list + detail routes for the other colliding prefixes stay on the SPA.
    [InlineData("/audio", false)]
    [InlineData("/audio/0d42e2e4-b181-4392-aae8-3c2184422a97", false)]
    [InlineData("/artists", false)]
    public void BackendRouteClassifierKeepsLowercaseSpaRoutesOnVite(string path, bool expected) {
        Assert.Equal(expected, SpaDevProxy.ShouldPassThroughToBackend(new PathString(path)));
    }

    [Fact]
    public async Task BackendPassThroughIgnoresBrowserAbortCancellation() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = new DefaultHttpContext {
            RequestAborted = cts.Token
        };

        await SpaDevProxy.InvokeBackendRequestAsync(
            context,
            () => throw new OperationCanceledException(cts.Token));
    }

    [Fact]
    public async Task BackendPassThroughPropagatesNonAbortCancellation() {
        var context = new DefaultHttpContext();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SpaDevProxy.InvokeBackendRequestAsync(
                context,
                () => throw new OperationCanceledException()));
    }
}
