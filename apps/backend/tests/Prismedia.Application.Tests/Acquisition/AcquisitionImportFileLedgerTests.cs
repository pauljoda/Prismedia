using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionImportFileLedgerTests {
    [Fact]
    public void PlacementCheckpointCreatesPrivacySafePendingEntries() {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "library"));
        var payload = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "download"));
        var checkpoint = new ImportPlacementCheckpoint(
            EntityKind.Movie,
            Guid.NewGuid(),
            root,
            payload,
            ImportMode.Copy,
            Path.Combine(root, "Movie"),
            Path.Combine(root, "Movie", "Movie.mkv"),
            "Imported",
            [new ImportPlacementCheckpointUnit(
                "feature/Movie.mkv",
                Path.Combine(payload, "feature", "Movie.mkv"),
                Path.Combine(root, "Movie", "Movie.mkv"),
                true)]);

        var ledger = AcquisitionImportFileLedger.Create(checkpoint);
        var file = Assert.Single(ledger.Files);

        Assert.Equal(AcquisitionImportPhase.Importing, ledger.Phase);
        Assert.Equal("feature/Movie.mkv", file.SourceRelativePath);
        Assert.Equal("Movie/Movie.mkv", file.DestinationRelativePath);
        Assert.Equal(AcquisitionImportFileRole.Media, file.Role);
        Assert.Equal(AcquisitionImportContentKind.Video, file.ContentKind);
        Assert.Equal(AcquisitionImportFileStatus.PendingImport, file.Status);
        Assert.Equal(AcquisitionImportDecision.PlaceNew, file.Decision);
        Assert.All(ledger.Files, entry => Assert.False(Path.IsPathFullyQualified(entry.SourceRelativePath)));
        Assert.All(ledger.Files, entry => Assert.False(Path.IsPathFullyQualified(entry.DestinationRelativePath!)));
    }

    [Fact]
    public void AdvancingAUnitRetainsStableIdentityAndMarksItImported() {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "library"));
        var payload = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "download"));
        var pending = new ImportPlacementCheckpointUnit(
            "book.epub",
            Path.Combine(payload, "book.epub"),
            Path.Combine(root, "Books", "book.epub"),
            true);
        var checkpoint = new ImportPlacementCheckpoint(
            EntityKind.Book, Guid.NewGuid(), root, payload, ImportMode.Move,
            Path.Combine(root, "Books"), Path.Combine(root, "Books", "book.epub"), "Imported", [pending]);
        var ledger = AcquisitionImportFileLedger.Create(checkpoint);

        var advanced = ledger.Advance("book.epub", "Books/book.epub");

        Assert.Equal(ledger.Files[0].Id, advanced.Files[0].Id);
        Assert.Equal(AcquisitionImportFileStatus.Imported, advanced.Files[0].Status);
        Assert.Equal("Books/book.epub", advanced.Files[0].DestinationRelativePath);
    }

    [Fact]
    public void SanitizerRemovesAbsolutePathsAndSecretsFromTechnicalErrors() {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "library"));
        var payload = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "download"));
        var unrelatedAbsolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "unrelated", "trace.log"));
        var error = $"Could not copy {Path.Combine(payload, "private", "movie.mkv")} to {Path.Combine(root, "Movies", "movie.mkv")}; trace={unrelatedAbsolutePath}; apiKey=secret-token";

        var sanitized = AcquisitionImportErrorSanitizer.Sanitize(error, payload, root);

        Assert.DoesNotContain(payload, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain(root, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain(unrelatedAbsolutePath, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", sanitized, StringComparison.Ordinal);
        Assert.Contains("private/movie.mkv", sanitized, StringComparison.Ordinal);
        Assert.Contains("Movies/movie.mkv", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void MergedLedgerRetainsImportedAndSkippedOutcomeDecisions() {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "library"));
        var payload = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "download"));
        var placed = new ImportPlacementCheckpointUnit(
            "album/new.flac",
            Path.Combine(payload, "album", "new.flac"),
            Path.Combine(root, "Artist", "Album", "new.flac"),
            true);
        var checkpoint = new ImportPlacementCheckpoint(
            EntityKind.AudioLibrary, Guid.NewGuid(), root, payload, ImportMode.Copy,
            Path.Combine(root, "Artist", "Album"), Path.Combine(root, "Artist", "Album"),
            "Imported", [placed]);
        var ledger = AcquisitionImportFileLedger.Create(checkpoint, [
            new MergedImportItem("album/new.flac", placed.TargetAbsolutePath, MergeFileAction.PlaceNew),
            new MergedImportItem("album/same.flac", Path.Combine(root, "Artist", "Album", "same.flac"), MergeFileAction.DropNotUpgrade),
            new MergedImportItem(
                "album/lower.flac",
                Path.Combine(root, "Artist", "Album", "lower.flac"),
                MergeFileAction.DropNotUpgrade,
                Path.Combine(root, "Artist", "Album", "owned.flac")),
            new MergedImportItem("album/change.mp3", Path.Combine(root, "Artist", "Album", "change.flac"), MergeFileAction.DropFormatChange)
        ]);

        Assert.Collection(
            ledger.Files,
            file => {
                Assert.Equal(AcquisitionImportFileStatus.PendingImport, file.Status);
                Assert.Equal(AcquisitionImportDecision.PlaceNew, file.Decision);
            },
            file => {
                Assert.Equal(AcquisitionImportFileStatus.Skipped, file.Status);
                Assert.Equal(AcquisitionImportDecision.SkipExisting, file.Decision);
            },
            file => {
                Assert.Equal(AcquisitionImportFileStatus.Skipped, file.Status);
                Assert.Equal(AcquisitionImportDecision.SkipNotUpgrade, file.Decision);
            },
            file => {
                Assert.Equal(AcquisitionImportFileStatus.Skipped, file.Status);
                Assert.Equal(AcquisitionImportDecision.HoldFormatChange, file.Decision);
            });
    }
}
