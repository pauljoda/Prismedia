using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class IntegrationGalleryOutputSetTests {
    private static IntegrationArtifact Page(string id, int? ordinal, string? group = "album") =>
        new(id, "selected", id + ".png", "image/png", 100, new string('a', 64), IntegrationArtifactRole.Content, group, ordinal);

    [Fact]
    public void SealedGalleryUsesDeclaredOrderAndAllowsOneImage() {
        var result = new IntegrationGalleryOutputSet([Page("last", 2), Page("first", 1)], "selected", 1000);
        Assert.Equal("album", result.GroupId);
        Assert.Equal(new[] { "first", "last" }, result.Artifacts.Select(a => a.Id));
        Assert.Single(new IntegrationGalleryOutputSet([Page("only", 1)], "selected", 1000).Artifacts);
    }

    [Theory]
    [InlineData("missing order")]
    [InlineData("gap")]
    [InlineData("duplicate order")]
    [InlineData("mixed groups")]
    [InlineData("unselected item")]
    [InlineData("sidecar")]
    [InlineData("unsupported image")]
    [InlineData("oversized image")]
    [InlineData("aggregate budget")]
    public void AmbiguousIncompleteOrUnboundedGalleryCannotImport(string problem) {
        var pages = new[] { Page("first", 1), Page("last", 2) };
        long limit = 1000;
        pages[1] = problem switch {
            "missing order" => Page("last", null, null),
            "gap" => Page("last", 3),
            "duplicate order" => Page("last", 1),
            "mixed groups" => Page("last", 2, "other"),
            "unselected item" => pages[1] with { ItemId = "unselected" },
            "sidecar" => pages[1] with { Role = Enum.GetValues<IntegrationArtifactRole>().First(role => role != IntegrationArtifactRole.Content) },
            "unsupported image" => pages[1] with { RelativePath = "page.svg" },
            "oversized image" => pages[1] with { SizeBytes = IntegrationMediaFormats.MaximumImageBytes + 1 },
            _ => pages[1]
        };
        if (problem == "aggregate budget") limit = 199;
        Assert.Throws<ArgumentException>(() => new IntegrationGalleryOutputSet(pages, "selected", limit));
    }
}
