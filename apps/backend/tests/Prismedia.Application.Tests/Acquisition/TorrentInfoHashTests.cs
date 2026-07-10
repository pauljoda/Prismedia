using System.Text;
using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class TorrentInfoHashTests {
    [Fact]
    public void ComputesHashFromExactTopLevelInfoDictionaryBytes() {
        var torrent = Encoding.ASCII.GetBytes("d8:announce3:url4:infod4:name4:testee");

        var result = TorrentInfoHash.TryComputeV1(torrent);

        Assert.Equal("1ade8a1a581f338e4fce4ce784da3f7d03f81f3a", result);
    }

    [Fact]
    public void RejectsAPureV2TorrentInsteadOfPersistingAFalseSha1NativeId() {
        var torrent = Encoding.ASCII.GetBytes(
            "d4:infod12:meta versioni2e9:file treedeee");

        var result = TorrentInfoHash.TryComputeV1(torrent);

        Assert.Null(result);
    }

    [Fact]
    public void UsesTheV1IdentityForAHybridTorrent() {
        var torrent = Encoding.ASCII.GetBytes(
            "d4:infod12:meta versioni2e6:pieces20:01234567890123456789ee");

        var result = TorrentInfoHash.TryComputeV1(torrent);

        Assert.Equal("ab226c9ed8ebbb8ee3314e38e722b71c82215e52", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("d4:name4:teste")]
    [InlineData("d4:info4:teste")]
    [InlineData("d4:infod4:name4:testeejunk")]
    [InlineData("d4:infod4:name4:testee4:infodee")]
    public void RejectsMalformedOrAmbiguousMetainfo(string payload) {
        var result = TorrentInfoHash.TryComputeV1(Encoding.ASCII.GetBytes(payload));

        Assert.Null(result);
    }
}
