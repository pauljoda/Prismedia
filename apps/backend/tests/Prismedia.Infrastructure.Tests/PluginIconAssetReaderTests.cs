using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginIconAssetReaderTests : IDisposable {
    private readonly string root = Path.Combine(Path.GetTempPath(), $"prismedia-plugin-icon-{Guid.NewGuid():N}");

    [Fact]
    public void ReadsContainedInertSvg() {
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "assets", "icon.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><path d=\"M0 0h1v1z\"/></svg>");

        var valid = PluginIconAssetReader.TryRead(root, "assets/icon.svg", out var icon);

        Assert.True(valid);
        Assert.NotNull(icon);
        Assert.Equal("image/svg+xml", icon.ContentType);
        Assert.Equal(64, icon.ETag.Length);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"https://tracker.example/pixel\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>")]
    public void RejectsActiveOrRemoteSvgContent(string svg) {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "icon.svg"), svg);

        Assert.False(PluginIconAssetReader.TryRead(root, "icon.svg", out _));
    }

    public void Dispose() {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
