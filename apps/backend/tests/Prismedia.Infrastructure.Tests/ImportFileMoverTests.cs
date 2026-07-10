using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Pins collision handling for ordinary and checkpoint-driven import placement.</summary>
public sealed class ImportFileMoverTests {
    [Fact]
    public void UnixCaseDistinctReservedTargetDoesNotForceASuffix() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"prismedia-case-{Guid.NewGuid():N}");
        var desired = Path.Combine(directory, "Episode.mkv");
        var caseDistinctReservation = Path.Combine(directory, "episode.mkv");

        var resolved = new ImportFileMover().ResolveExactTargetPath(
            desired,
            [caseDistinctReservation]);

        Assert.Equal(desired, resolved);
    }

    [Fact]
    public async Task ResolveExactTargetPathSkipsFilesAndTargetsReservedByTheSameBatch() {
        var root = Directory.CreateTempSubdirectory("prismedia-import-resolver-test");
        try {
            var desired = await WriteAsync(root, "library/episode.mkv", "existing-payload");
            var reserved = Path.Combine(root.FullName, "library", "episode (2).mkv");

            var resolved = new ImportFileMover().ResolveExactTargetPath(desired, [reserved]);

            Assert.Equal(Path.Combine(root.FullName, "library", "episode (3).mkv"), resolved);
            Assert.False(File.Exists(resolved), "resolving a target must not mutate the library");
        } finally {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlaceExactAsyncUsesTheSuppliedTargetPath() {
        var root = Directory.CreateTempSubdirectory("prismedia-exact-import-mover-test");
        try {
            var source = await WriteAsync(root, "downloads/episode.mkv", "new-payload");
            var target = Path.Combine(root.FullName, "library", "Series", "episode.mkv");

            var placed = await new ImportFileMover().PlaceExactAsync(
                new ResolvedImportItem(source, target),
                ImportMode.Copy,
                CancellationToken.None);

            Assert.Equal(target, placed);
            Assert.Equal("new-payload", await File.ReadAllTextAsync(target));
        } finally {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlaceAsyncSuffixesACollidingTargetWithoutOverwritingIt() {
        var root = Directory.CreateTempSubdirectory("prismedia-import-mover-test");
        try {
            var source = await WriteAsync(root, "downloads/episode.mkv", "new-payload");
            var target = await WriteAsync(root, "library/episode.mkv", "existing-payload");

            var placed = await new ImportFileMover().PlaceAsync(
                new ResolvedImportItem(source, target),
                ImportMode.Copy,
                CancellationToken.None);

            Assert.Equal(Path.Combine(root.FullName, "library", "episode (2).mkv"), placed);
            Assert.Equal("existing-payload", await File.ReadAllTextAsync(target));
            Assert.Equal("new-payload", await File.ReadAllTextAsync(placed));
        } finally {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task CancelledCopyNeverPublishesOrLeavesAStagedMediaFile() {
        var root = Directory.CreateTempSubdirectory("prismedia-cancelled-import-mover-test");
        try {
            var source = await WriteAsync(root, "downloads/episode.mkv", "new-payload");
            var target = Path.Combine(root.FullName, "library", "episode.mkv");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImportFileMover().PlaceExactAsync(
                new ResolvedImportItem(source, target),
                ImportMode.Copy,
                cancelled.Token));

            Assert.False(File.Exists(target));
            Assert.Equal("new-payload", await File.ReadAllTextAsync(source));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(target)!,
                "*.prismedia-import",
                SearchOption.TopDirectoryOnly));
        } finally {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(ImportMode.Copy)]
    [InlineData(ImportMode.Hardlink)]
    [InlineData(ImportMode.Move)]
    public async Task PlaceExactAsyncRefusesACollidingTargetWithoutSuffixingOrOverwritingIt(ImportMode mode) {
        var root = Directory.CreateTempSubdirectory("prismedia-exact-import-mover-test");
        try {
            var source = await WriteAsync(root, "downloads/episode.mkv", "new-payload");
            var target = await WriteAsync(root, "library/episode.mkv", "existing-payload");

            await Assert.ThrowsAsync<IOException>(() => new ImportFileMover().PlaceExactAsync(
                new ResolvedImportItem(source, target),
                mode,
                CancellationToken.None));

            Assert.Equal("existing-payload", await File.ReadAllTextAsync(target));
            Assert.Equal("new-payload", await File.ReadAllTextAsync(source));
            Assert.False(File.Exists(Path.Combine(root.FullName, "library", "episode (2).mkv")));
        } finally {
            root.Delete(recursive: true);
        }
    }

    private static async Task<string> WriteAsync(DirectoryInfo root, string relativePath, string contents) {
        var path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
        return path;
    }
}
