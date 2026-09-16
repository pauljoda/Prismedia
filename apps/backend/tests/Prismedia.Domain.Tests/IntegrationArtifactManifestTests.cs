using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class IntegrationArtifactManifestTests {
    private static IntegrationArtifact Artifact => new("artifact", "source-item", "chapter/issue.cbz", "application/vnd.comicbook+zip", 100,
        new string('a', 64), IntegrationArtifactRole.Content);

    [Theory]
    [InlineData("../outside.cbz")]
    [InlineData("/absolute.cbz")]
    [InlineData("chapter/../../outside.cbz")]
    [InlineData("C:\\outside.cbz")]
    [InlineData("chapter\\issue.cbz")]
    public void ProviderPathsCannotEscapeOrDependOnHostPlatform(string path) => Assert.Throws<ArgumentException>(() =>
        new IntegrationArtifactManifest("job", "revision", true, 1, [Artifact with { RelativePath = path }]));

    [Fact]
    public void MissingPagesAndUnsealedOutputsAreNotCompleteManifests() {
        Assert.Throws<ArgumentException>(() => new IntegrationArtifactManifest("job", "revision", false, 1, [Artifact]));
        Assert.Throws<ArgumentException>(() => new IntegrationArtifactManifest("job", "revision", true, 2, [Artifact]));
    }

    [Fact]
    public void DuplicateIdsOrPortablePathsAndMissingHashesAreRejected() {
        Assert.Throws<ArgumentException>(() => new IntegrationArtifactManifest("job", "revision", true, 2, [Artifact, Artifact with { RelativePath = "other.cbz" }]));
        Assert.Throws<ArgumentException>(() => new IntegrationArtifactManifest("job", "revision", true, 2, [Artifact, Artifact with { Id = "other", RelativePath = "CHAPTER/ISSUE.CBZ" }]));
        Assert.Throws<ArgumentException>(() => new IntegrationArtifactManifest("job", "revision", true, 1, [Artifact with { Sha256 = "unknown" }]));
    }

    [Fact]
    public void OrderedGalleryPagesRetainExplicitOneBasedPositions() {
        var first = Artifact with { GroupId = "gallery", Ordinal = 2 };
        var second = Artifact with { Id = "second", RelativePath = "other.cbz", GroupId = "gallery", Ordinal = 1 };
        var manifest = new IntegrationArtifactManifest("job", "revision", true, 2, [first, second]);
        Assert.Equal([2, 1], manifest.Artifacts.Select(artifact => artifact.Ordinal));
        Assert.Throws<ArgumentException>(() => new IntegrationArtifactManifest("job", "revision", true, 2, [first, second with { Ordinal = 2 }]));
        Assert.Throws<ArgumentException>(() => new IntegrationArtifactManifest("job", "revision", true, 1, [first with { Ordinal = 0 }]));
    }
}
