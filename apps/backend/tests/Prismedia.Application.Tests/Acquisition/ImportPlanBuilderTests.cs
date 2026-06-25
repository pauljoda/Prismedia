using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ImportPlanBuilderTests {
    private const string Template = "{Author}/{Title} ({Year})/{Title}{ - Volume}.{ext}";

    private static ImportTemplateContext Context(string title = "Project Hail Mary", string? author = "Andy Weir", int? year = 2021) =>
        new(title, author, year);

    [Fact]
    public void SingleEpubRendersFullTemplatePath() {
        var plan = ImportPlanBuilder.Plan(["Project Hail Mary.epub"], Context(), Template);

        Assert.False(plan.Blocked);
        var item = Assert.Single(plan.Items);
        Assert.Equal("Andy Weir/Project Hail Mary (2021)/Project Hail Mary.epub", item.TargetRelativePath);
    }

    [Fact]
    public void MissingYearDropsEmptyParentheses() {
        var plan = ImportPlanBuilder.Plan(["Book.epub"], Context(title: "Some Book", author: "An Author", year: null), Template);

        var item = Assert.Single(plan.Items);
        Assert.Equal("An Author/Some Book/Some Book.epub", item.TargetRelativePath);
    }

    [Fact]
    public void MissingAuthorDropsAuthorSegment() {
        var plan = ImportPlanBuilder.Plan(["Book.pdf"], Context(title: "Lonely Book", author: null, year: 2000), Template);

        var item = Assert.Single(plan.Items);
        Assert.Equal("Lonely Book (2000)/Lonely Book.pdf", item.TargetRelativePath);
    }

    [Fact]
    public void IllegalCharactersAreSanitized() {
        var plan = ImportPlanBuilder.Plan(["x.epub"], Context(title: "A/B: C?", author: "D|E"), Template);

        var item = Assert.Single(plan.Items);
        Assert.DoesNotContain(':', item.TargetRelativePath.Replace("/", string.Empty));
        Assert.DoesNotContain('?', item.TargetRelativePath);
        Assert.DoesNotContain('|', item.TargetRelativePath);
    }

    [Fact]
    public void MultipleComicArchivesGoUnderRenderedFolder() {
        var plan = ImportPlanBuilder.Plan(
            ["Vol 1.cbz", "Vol 2.cbz"],
            Context(title: "Saga", author: "BKV", year: 2012),
            Template);

        Assert.False(plan.Blocked);
        Assert.Equal(2, plan.Items.Count);
        Assert.All(plan.Items, item => Assert.StartsWith("BKV/Saga (2012)/", item.TargetRelativePath));
        Assert.Contains(plan.Items, item => item.TargetRelativePath.EndsWith("Vol 1.cbz"));
    }

    [Fact]
    public void NoSupportedFilesBlocks() {
        var plan = ImportPlanBuilder.Plan(["readme.txt", "cover.jpg"], Context(), Template);

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.NoSupportedPayload, plan.BlockReason);
    }

    [Fact]
    public void MultipleStandaloneBooksBlockAsAmbiguous() {
        var plan = ImportPlanBuilder.Plan(["a.epub", "b.epub"], Context(), Template);

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.AmbiguousMultiplePrimaries, plan.BlockReason);
    }

    [Fact]
    public void FormatVariantsOfOneBookPickEpub() {
        // A common book release bundles several formats of the same title.
        var plan = ImportPlanBuilder.Plan(
            ["The Anxious Generation.pdf", "The Anxious Generation.epub", "The Anxious Generation.mobi"],
            Context(title: "The Anxious Generation", author: "Jonathan Haidt", year: 2024),
            Template);

        Assert.False(plan.Blocked);
        var item = Assert.Single(plan.Items);
        Assert.EndsWith(".epub", item.TargetRelativePath);
        Assert.Equal("Jonathan Haidt/The Anxious Generation (2024)/The Anxious Generation.epub", item.TargetRelativePath);
    }

    [Fact]
    public void BookMixedWithArchiveBlocks() {
        var plan = ImportPlanBuilder.Plan(["a.epub", "b.cbz"], Context(), Template);

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.MixedPayload, plan.BlockReason);
    }

    [Fact]
    public void UnsupportedSidecarFilesAreIgnoredAlongsideTheBook() {
        var plan = ImportPlanBuilder.Plan(["book.epub", "metadata.opf", "cover.jpg"], Context(), Template);

        Assert.False(plan.Blocked);
        Assert.Single(plan.Items);
    }
}
