using System.Security.Cryptography;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Atomic, non-destructive media placement inside a frozen library boundary.</summary>
public sealed class IntegrationImportPlacement(ILibraryFileMutationGuard mutations) : IIntegrationImportPlacement {
    /// <inheritdoc />
    public async Task<string> PlaceAsync(Guid operationId, IntegrationTransferPlan plan, LibraryRootData root,
        VerifiedIntegrationArtifact artifact, CancellationToken cancellationToken) {
        await using var protection = await mutations.EnterAsync([root.Path], cancellationToken);
        var rootPath = Path.GetFullPath(root.Path);
        if (operationId == Guid.Empty || root.Id != plan.LibraryRootId || !IntegrationMediaFormats.SupportsRoot(plan.EntityKind, root)
            || !FileSystemPathComparison.Comparer.Equals(rootPath, Path.GetFullPath(plan.LibraryPath)) || !Directory.Exists(rootPath))
            throw new InvalidDataException("The accepted destination library is unavailable or has moved.");
        if (!IntegrationMediaFormats.IsSupported(plan.EntityKind, artifact.FileName)
            || artifact.SizeBytes <= 0 || artifact.Sha256 is not { Length: 64 } || !artifact.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The media file has no valid placement evidence.");
        RejectLink(rootPath);
        var artifactKey = IntegrationPublicationNames.ArtifactKey(artifact.ArtifactId);
        var target = Path.Combine(rootPath, IntegrationPublicationNames.FileName(operationId, plan.Title, artifact.ArtifactId, artifact.FileName));
        var pending = Path.Combine(rootPath, $".prismedia-{operationId:N}-{artifactKey}.part");
        RejectLink(target);
        RejectLink(pending);
        if (File.Exists(target)) { await VerifyAsync(target, artifact, cancellationToken); return target; }
        RejectLink(artifact.Path);
        await using (var output = new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 128 * 1024, true)) {
            output.SetLength(0);
            await using var input = new FileStream(artifact.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            if (input.Length != artifact.SizeBytes) throw new InvalidDataException("Staged publication bytes changed before placement.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            while (true) {
                var count = await input.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                if (output.Position + count > artifact.SizeBytes) throw new InvalidDataException("Staged publication bytes changed before placement.");
                hash.AppendData(buffer.AsSpan(0, count));
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            if (output.Length != artifact.SizeBytes || !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged publication bytes changed before placement.");
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        RejectLink(rootPath);
        RejectLink(target);
        RejectLink(pending);
        try { File.Move(pending, target); }
        catch (IOException) when (File.Exists(target)) { await VerifyAsync(target, artifact, cancellationToken); }
        return target;
    }

    private static async Task VerifyAsync(string path, VerifiedIntegrationArtifact artifact, CancellationToken cancellationToken) {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        if (stream.Length != artifact.SizeBytes || !Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An existing destination file differs from the accepted publication. It was preserved.");
    }

    private static void RejectLink(string path) {
        if (new FileInfo(path).LinkTarget is not null || (File.Exists(path) || Directory.Exists(path)) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Publication placement cannot traverse filesystem links.");
    }
}
