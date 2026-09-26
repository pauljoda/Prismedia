using System.Security.Cryptography;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Media.Processing;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationGalleryPlacementTests : IDisposable {
    private readonly string workspace = Directory.CreateTempSubdirectory("prismedia-gallery-placement-").FullName;
    private readonly IntegrationGalleryPlacement placement = new(new TestFileMutationGuard());
    private async Task<IntegrationGalleryPlacementRequest> RequestAsync() {
        var library = Directory.CreateDirectory(Path.Combine(workspace, "library")).FullName;
        var root = new LibraryRootData(Guid.NewGuid(), library, "Galleries", true, true, false, true, false, false, false, false);
        var operation = Guid.NewGuid();
        var plan = new IntegrationTransferPlan("Gallery", EntityKind.Gallery, root.Id, library, "owner", new string('a', 64));
        var artifacts = new List<IntegrationArtifact>();
        var verified = new List<VerifiedIntegrationArtifact>();
        for (var ordinal = 1; ordinal <= 2; ordinal++) {
            var id = "page-" + ordinal;
            var bytes = System.Text.Encoding.UTF8.GetBytes("original page " + ordinal);
            var path = Path.Combine(workspace, id + ".png");
            await File.WriteAllBytesAsync(path, bytes);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            artifacts.Add(new(id, "selected", "source/" + id + ".png", "image/png", bytes.Length, hash, IntegrationArtifactRole.Content, "group", ordinal));
            verified.Add(new(id, path, bytes.Length, hash, id + ".png"));
        }
        return new(operation, plan, root, new(artifacts.AsEnumerable().Reverse().ToArray(), "selected", 1000), verified);
    }
    [Fact]
    public async Task WholeGalleryAppearsInDeclaredOrderAndReplayKeepsExactPaths() {
        var request = await RequestAsync();
        var placed = await placement.PlaceAsync(request, default);
        Assert.Equal(request.Root.Path, Path.GetDirectoryName(placed.FolderPath));
        Assert.Equal(new[] { "page-1", "page-2" }, placed.Files.OrderBy(pair => pair.Value).Select(pair => pair.Key));
        Assert.Equal(2, Directory.GetFiles(placed.FolderPath).Length);
        var replay = await placement.PlaceAsync(request, default);
        Assert.Equal(placed.FolderPath, replay.FolderPath);
        foreach (var file in request.VerifiedArtifacts)
            Assert.Equal(await File.ReadAllBytesAsync(file.Path), await File.ReadAllBytesAsync(replay.Files[file.ArtifactId]));
    }
    [Fact]
    public async Task PublishedGalleryRecoversAfterAllStagingIsLost() {
        var request = await RequestAsync();
        var placed = await placement.PlaceAsync(request, default);
        foreach (var staged in request.VerifiedArtifacts) File.Delete(staged.Path);

        var recovered = await placement.ReadPlacedAsync(request.OperationId, request.Plan, request.Root, request.Outputs, default);

        Assert.NotNull(recovered);
        Assert.Equal(placed.FolderPath, recovered.FolderPath);
        Assert.Equal(placed.Files, recovered.Files);
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.ReadPlacedAsync(request.OperationId, request.Plan,
            request.Root with { Recursive = false }, request.Outputs, default));
    }
    [Fact]
    public async Task GalleryRecoveryRequiresEveryExactFileAndNoAdditionalContent() {
        var request = await RequestAsync();
        var placed = await placement.PlaceAsync(request, default);
        File.Delete(placed.Files["page-1"]);
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.ReadPlacedAsync(request.OperationId,
            request.Plan, request.Root, request.Outputs, default));
        await File.WriteAllTextAsync(placed.Files["page-1"], "original page 1");
        await File.WriteAllTextAsync(Path.Combine(placed.FolderPath, "unexpected.png"), "unexpected");
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.ReadPlacedAsync(request.OperationId,
            request.Plan, request.Root, request.Outputs, default));
    }
    [Fact]
    public async Task FailedCopyNeverPublishesAPartialGalleryToScanning() {
        var request = await RequestAsync();
        var original = await File.ReadAllBytesAsync(request.VerifiedArtifacts[1].Path);
        await File.WriteAllTextAsync(request.VerifiedArtifacts[1].Path, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(request, default));
        var discovered = await new FileDiscoveryService().DiscoverFilesAsync(request.Root.Path,
            new HashSet<string>([".png"]), true, null, default);
        Assert.Empty(discovered);
        await File.WriteAllBytesAsync(request.VerifiedArtifacts[1].Path, original);
        var recovered = await placement.PlaceAsync(request, default);
        Assert.Equal(2, Directory.GetFiles(recovered.FolderPath).Length);
        Assert.DoesNotContain(Directory.GetDirectories(request.Root.Path), path => Path.GetFileName(path).StartsWith('.'));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingChangedOrAdditionalFilesArePreservedAndBlockReplay(bool additionalFile) {
        var request = await RequestAsync();
        var placed = await placement.PlaceAsync(request, default);
        var changed = additionalFile ? Path.Combine(placed.FolderPath, "user.png") : placed.Files["page-1"];
        await File.WriteAllTextAsync(changed, "user bytes");
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(request, default));
        Assert.Equal("user bytes", await File.ReadAllTextAsync(changed));
    }
    [Fact]
    public async Task NonrecursiveLibrariesCannotLoseTheGalleryOnALaterScan() {
        var request = await RequestAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(request with { Root = request.Root with { Recursive = false } }, default));
        Assert.Empty(Directory.GetFileSystemEntries(request.Root.Path));
    }
    [Fact]
    public async Task LinkedGalleryDestinationCannotWriteOutsideTheAcceptedLibrary() {
        var request = await RequestAsync();
        var outside = Directory.CreateDirectory(Path.Combine(workspace, "outside")).FullName;
        var target = Path.Combine(request.Root.Path, IntegrationGalleryNames.FolderName(request.OperationId, request.Plan.Title, request.Outputs.GroupId));
        Directory.CreateSymbolicLink(target, outside);
        await Assert.ThrowsAsync<InvalidDataException>(() => placement.PlaceAsync(request, default));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Directory.Delete(target);
    }
    public void Dispose() => Directory.Delete(workspace, true);
}
