using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Pins the remote-path rewrite: longest matching prefix wins, matches respect path-segment
/// boundaries (never partial-name matches), Windows-style client paths translate, and unmapped
/// paths (or mapping-free deployments) pass through untouched.
/// </summary>
public sealed class RemotePathMapperTests {
    private static readonly Guid ClientId = Guid.NewGuid();

    private static RemotePathMapper Mapper(params (string Remote, string Local)[] mappings) =>
        new(new FakeStore(mappings.Select(m => new RemotePathMappingView(Guid.NewGuid(), ClientId, m.Remote, m.Local)).ToArray()));

    [Fact]
    public async Task RewritesTheMatchingPrefix() {
        var mapper = Mapper(("/downloads", "/mnt/media/downloads"));
        Assert.Equal(
            $"/mnt/media/downloads{Path.DirectorySeparatorChar}complete{Path.DirectorySeparatorChar}My.Show",
            await mapper.ToLocalAsync(ClientId, "/downloads/complete/My.Show", CancellationToken.None));
    }

    [Fact]
    public async Task TheLongestPrefixWins() {
        var mapper = Mapper(("/downloads", "/wrong"), ("/downloads/complete", "/mnt/complete"));
        Assert.Equal(
            $"/mnt/complete{Path.DirectorySeparatorChar}My.Show",
            await mapper.ToLocalAsync(ClientId, "/downloads/complete/My.Show", CancellationToken.None));
    }

    [Fact]
    public async Task NeverMatchesAPartialSegmentName() {
        var mapper = Mapper(("/downloads", "/mnt/downloads"));
        Assert.Equal("/downloads-archive/My.Show", await mapper.ToLocalAsync(ClientId, "/downloads-archive/My.Show", CancellationToken.None));
    }

    [Fact]
    public async Task WindowsStyleClientPathsTranslate() {
        var mapper = Mapper((@"C:\Downloads", "/mnt/downloads"));
        Assert.Equal(
            $"/mnt/downloads{Path.DirectorySeparatorChar}My.Show",
            await mapper.ToLocalAsync(ClientId, @"C:\Downloads\My.Show", CancellationToken.None));
    }

    [Fact]
    public async Task UnmappedPathsAndOtherClientsPassThrough() {
        var mapper = Mapper(("/downloads", "/mnt/downloads"));
        Assert.Equal("/elsewhere/My.Show", await mapper.ToLocalAsync(ClientId, "/elsewhere/My.Show", CancellationToken.None));
        Assert.Equal("/downloads/My.Show", await mapper.ToLocalAsync(Guid.NewGuid(), "/downloads/My.Show", CancellationToken.None));
        Assert.Null(await mapper.ToLocalAsync(ClientId, null, CancellationToken.None));
    }

    private sealed class FakeStore(IReadOnlyList<RemotePathMappingView> mappings) : IRemotePathMappingStore {
        public Task<IReadOnlyList<RemotePathMappingView>> ListForClientAsync(Guid downloadClientConfigId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RemotePathMappingView>>(
                mappings.Where(m => m.DownloadClientConfigId == downloadClientConfigId)
                    .OrderByDescending(m => m.RemotePath.Length)
                    .ToArray());
        public Task<IReadOnlyList<RemotePathMappingView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RemotePathMappingView> SaveAsync(RemotePathMappingSaveRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
