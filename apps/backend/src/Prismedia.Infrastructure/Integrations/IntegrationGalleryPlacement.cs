using System.Security.Cryptography;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Builds a hidden verified group and publishes it through a same-filesystem directory rename.</summary>
public sealed class IntegrationGalleryPlacement(ILibraryFileMutationGuard mutations) : IIntegrationGalleryPlacement {
    /// <inheritdoc />
    public async Task<PlacedIntegrationGallery?> ReadPlacedAsync(Guid operationId, IntegrationTransferPlan plan, LibraryRootData root,
        IntegrationGalleryOutputSet outputs, CancellationToken cancellationToken) {
        await using var protection = await mutations.EnterAsync([root.Path], cancellationToken);
        var rootPath = ValidateDestination(operationId, plan, root);
        ValidateOutputs(outputs);
        var target = Path.Combine(rootPath, IntegrationGalleryNames.FolderName(operationId, plan.Title, outputs.GroupId));
        RejectLink(target);
        if (File.Exists(target)) throw new InvalidDataException("A gallery placement directory conflicts with an existing file.");
        if (!Directory.Exists(target)) return null;
        await VerifyDirectoryAsync(target, outputs, cancellationToken);
        return Result(target, outputs);
    }

    /// <inheritdoc />
    public async Task<PlacedIntegrationGallery> PlaceAsync(IntegrationGalleryPlacementRequest request, CancellationToken cancellationToken) {
        var (operationId, plan, root, outputs, verified) = request;
        await using var protection = await mutations.EnterAsync([root.Path], cancellationToken);
        var rootPath = ValidateDestination(operationId, plan, root);
        ValidateOutputs(outputs);
        if (verified.Count != outputs.Artifacts.Count || verified.Select(file => file.ArtifactId).Distinct().Count() != verified.Count)
            throw new InvalidDataException("Every gallery member needs exact verified staging evidence.");
        var byId = verified.ToDictionary(file => file.ArtifactId);
        foreach (var artifact in outputs.Artifacts) {
            if (!byId.TryGetValue(artifact.Id, out var file) || file.SizeBytes != artifact.SizeBytes
                || !artifact.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)
                || !Path.GetExtension(artifact.RelativePath).Equals(Path.GetExtension(file.FileName), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Gallery staging differs from the accepted manifest.");
        }
        var target = Path.Combine(rootPath, IntegrationGalleryNames.FolderName(operationId, plan.Title, outputs.GroupId));
        var pending = Path.Combine(rootPath, $".prismedia-{operationId:N}-gallery.part");
        RejectLink(rootPath); RejectLink(target); RejectLink(pending);
        if (File.Exists(target) || File.Exists(pending)) throw new InvalidDataException("A gallery placement directory conflicts with an existing file.");
        if (Directory.Exists(target)) { await VerifyDirectoryAsync(target, outputs, cancellationToken); return Result(target, outputs); }
        Directory.CreateDirectory(pending);
        foreach (var artifact in outputs.Artifacts) {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(rootPath); RejectLink(pending);
            var path = Path.Combine(pending, IntegrationGalleryNames.FileName(artifact));
            await CopyAsync(byId[artifact.Id], path, cancellationToken);
        }
        await VerifyDirectoryAsync(pending, outputs, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        RejectLink(rootPath); RejectLink(pending); RejectLink(target);
        try { Directory.Move(pending, target); }
        catch (IOException) when (Directory.Exists(target)) { await VerifyDirectoryAsync(target, outputs, cancellationToken); }
        return Result(target, outputs);
    }

    private static PlacedIntegrationGallery Result(string folder, IntegrationGalleryOutputSet outputs) =>
        new(folder, outputs.Artifacts.ToDictionary(artifact => artifact.Id, artifact => Path.Combine(folder, IntegrationGalleryNames.FileName(artifact))));

    private static async Task VerifyDirectoryAsync(string folder, IntegrationGalleryOutputSet outputs, CancellationToken token) {
        RejectLink(folder);
        var expected = Result(folder, outputs).Files;
        var allowed = expected.Values.ToHashSet(FileSystemPathComparison.Comparer);
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder)) {
            if (!allowed.Contains(entry) || Directory.Exists(entry))
                throw new InvalidDataException("The gallery contains additional content. It was preserved for review.");
        }
        foreach (var artifact in outputs.Artifacts) {
            var file = new VerifiedIntegrationArtifact(artifact.Id, expected[artifact.Id], artifact.SizeBytes,
                artifact.Sha256.ToLowerInvariant(), Path.GetFileName(artifact.RelativePath));
            await VerifyFileAsync(file.Path, file, token);
        }
    }

    private static string ValidateDestination(Guid operationId, IntegrationTransferPlan plan, LibraryRootData root) {
        var rootPath = Path.GetFullPath(root.Path);
        if (operationId == Guid.Empty || plan.EntityKind != EntityKind.Gallery || root.Id != plan.LibraryRootId
            || !root.Enabled || root.IsReadOnly || !root.ScanImages || !root.Recursive || !Directory.Exists(rootPath)
            || !FileSystemPathComparison.Equals(rootPath, Path.GetFullPath(plan.LibraryPath)))
            throw new InvalidDataException("The accepted recursive image library is unavailable or has changed.");
        RejectLink(rootPath);
        return rootPath;
    }

    private static void ValidateOutputs(IntegrationGalleryOutputSet outputs) {
        if (outputs.Artifacts.Count == 0 || outputs.Artifacts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != outputs.Artifacts.Count)
            throw new InvalidDataException("The accepted gallery output set is invalid.");
    }

    private static async Task CopyAsync(VerifiedIntegrationArtifact artifact, string target, CancellationToken token) {
        var pending = target + ".part";
        RejectLink(target); RejectLink(pending); RejectLink(artifact.Path);
        if (File.Exists(target)) { await VerifyFileAsync(target, artifact, token); return; }
        await using (var output = new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 128 * 1024, true)) {
            output.SetLength(0);
            await using var input = new FileStream(artifact.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            if (input.Length != artifact.SizeBytes) throw new InvalidDataException("Staged gallery bytes changed before placement.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            while (true) {
                var count = await input.ReadAsync(buffer, token);
                if (count == 0) break;
                if (output.Position + count > artifact.SizeBytes) throw new InvalidDataException("Staged gallery bytes exceed their accepted length.");
                hash.AppendData(buffer.AsSpan(0, count));
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
            if (output.Length != artifact.SizeBytes || !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged gallery bytes differ from their accepted hash.");
            await output.FlushAsync(token); output.Flush(flushToDisk: true);
        }
        RejectLink(target); RejectLink(pending);
        File.Move(pending, target);
    }

    private static async Task VerifyFileAsync(string path, VerifiedIntegrationArtifact artifact, CancellationToken token) {
        RejectLink(path);
        if (!File.Exists(path)) throw new InvalidDataException("An accepted gallery member is missing.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        if (stream.Length != artifact.SizeBytes || !Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An existing gallery member differs from the accepted bytes. It was preserved.");
    }
    private static void RejectLink(string path) {
        if (new FileInfo(path).LinkTarget is not null || (File.Exists(path) || Directory.Exists(path)) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Gallery placement cannot traverse filesystem links.");
    }
}
