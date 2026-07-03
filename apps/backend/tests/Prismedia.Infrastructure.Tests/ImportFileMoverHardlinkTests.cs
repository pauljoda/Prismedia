using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Pins hardlink import placement: within one filesystem the target is a true hard link (same inode —
/// the source keeps seeding while the library file exists at zero extra cost), the source always
/// survives, and content matches. The cross-device copy fallback can't be exercised in a unit test,
/// but the code path is shared with copy mode.
/// </summary>
public sealed class ImportFileMoverHardlinkTests {
    [Fact]
    public async Task HardlinkPlacesALinkAndKeepsTheSource() {
        var root = Directory.CreateTempSubdirectory("prismedia-hardlink-test");
        try {
            var source = Path.Combine(root.FullName, "downloads", "book.epub");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllTextAsync(source, "payload-bytes");
            var target = Path.Combine(root.FullName, "library", "Author", "book.epub");

            var placed = await new ImportFileMover().PlaceAsync(
                new ResolvedImportItem(source, target), ImportMode.Hardlink, CancellationToken.None);

            Assert.Equal(target, placed);
            Assert.True(File.Exists(source), "the source must keep seeding");
            Assert.Equal("payload-bytes", await File.ReadAllTextAsync(placed));

            // Same inode: appending through one path is visible through the other (definitive hard-link proof).
            await File.AppendAllTextAsync(source, "!");
            Assert.Equal("payload-bytes!", await File.ReadAllTextAsync(placed));
        } finally {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TryCreateRefusesAcrossMissingSource() {
        Assert.False(HardLink.TryCreate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }
}
