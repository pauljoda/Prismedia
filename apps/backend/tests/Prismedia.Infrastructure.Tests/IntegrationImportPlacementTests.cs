using System.Security.Cryptography;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationImportPlacementTests : IDisposable {
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "prismedia-placement-" + Guid.NewGuid().ToString("N"));
    private readonly IntegrationImportPlacement placement = new(new TestFileMutationGuard());
    private readonly Guid operation = Guid.NewGuid();

    [Fact]
    public async Task RepeatedPlacementReusesExactBytesAndNeverReplacesConflictingFile() {
        var (plan, root, artifact) = await FixtureAsync();
        var path = await placement.PlaceAsync(operation, plan, root, artifact, default);
        Assert.Equal(root.Path, Path.GetDirectoryName(path));
        Assert.Equal(path, await placement.PlaceAsync(operation, plan, root, artifact, default));
        await File.WriteAllTextAsync(path, "user replacement");
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(operation, plan, root, artifact, default));
        Assert.Equal("user replacement", await File.ReadAllTextAsync(path));
        Assert.True(File.Exists(artifact.Path));
    }

    [Fact]
    public async Task ImagePlacementRequiresImageScanningAndRejectsBookOnlyRoots() {
        var (plan, root, artifact) = await FixtureAsync();
        plan = plan with { EntityKind = EntityKind.Image };
        artifact = artifact with { FileName = "image.png" };
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(operation, plan, root, artifact, default));
        root = root with { ScanBooks = false, ScanImages = true };
        var path = await placement.PlaceAsync(operation, plan, root, artifact, default);
        Assert.EndsWith(".png", path);
        Assert.Equal(await File.ReadAllBytesAsync(artifact.Path), await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ChangedAndMissingLibraryCannotRedirectOrRecreateDestination() {
        var (plan, root, artifact) = await FixtureAsync();
        var moved = Path.Combine(workspace, "moved");
        Directory.CreateDirectory(moved);
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(operation, plan, root with { Path = moved }, artifact, default));
        Directory.Delete(root.Path);
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(operation, plan, root, artifact, default));
        Assert.False(Directory.Exists(root.Path));
        Assert.Empty(Directory.GetFiles(moved));
    }

    [Fact]
    public async Task StagingMutationCannotPublishLibraryFile() {
        var (plan, root, artifact) = await FixtureAsync();
        await File.WriteAllTextAsync(artifact.Path, "changed staged publication");
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(operation, plan, root, artifact, default));
        Assert.Empty(Directory.GetFiles(root.Path, "*.epub"));
    }

    [Fact]
    public async Task LinkedLibraryCannotWriteOutsideAcceptedDirectory() {
        if (OperatingSystem.IsWindows()) return;
        var (plan, root, artifact) = await FixtureAsync();
        var outside = Path.Combine(workspace, "outside");
        Directory.CreateDirectory(outside);
        Directory.Delete(root.Path);
        Directory.CreateSymbolicLink(root.Path, outside);
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(operation, plan, root, artifact, default));
        Assert.Empty(Directory.GetFiles(outside));
    }

    private async Task<(IntegrationTransferPlan, LibraryRootData, VerifiedIntegrationArtifact)> FixtureAsync() {
        var library = Path.Combine(workspace, "library");
        Directory.CreateDirectory(library);
        var staged = Path.Combine(workspace, "staged.epub");
        var bytes = "verified publication"u8.ToArray();
        await File.WriteAllBytesAsync(staged, bytes);
        var root = new LibraryRootData(Guid.NewGuid(), library, "Books", true, false, false, false, false, true, false, false);
        var plan = new IntegrationTransferPlan("../../Unsafe: title", EntityKind.Book, root.Id, library, "owner", new string('a', 64));
        return (plan, root, new("artifact", staged, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), "book.epub"));
    }

    public void Dispose() { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
}
