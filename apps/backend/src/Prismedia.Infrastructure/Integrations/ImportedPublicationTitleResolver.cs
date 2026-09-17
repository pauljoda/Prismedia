using System.Security.Cryptography;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Uses the existing durable import journal, exact accepted path, and verified bytes rather than heuristically stripping filename suffixes.</summary>
public sealed class ImportedPublicationTitleResolver(IIntegrationTransferStore transfers) : IImportedPublicationTitleResolver {
    /// <inheritdoc />
    public async Task<string?> ResolveAsync(Guid libraryRootId, EntityKind kind, string sourcePath, CancellationToken cancellationToken) {
        if (kind == EntityKind.Gallery)
            return await ResolveGalleryAsync(libraryRootId, sourcePath, null, cancellationToken);
        if (kind == EntityKind.Image && await ResolveGalleryAsync(libraryRootId, Path.GetDirectoryName(sourcePath)!, sourcePath, cancellationToken) is { } memberTitle)
            return memberTitle;
        if (IntegrationPublicationNames.CandidateOperation(sourcePath) is not { } id) return null;
        var work = await transfers.FindAsync(id, cancellationToken);
        if (work is null || work.Plan.LibraryRootId != libraryRootId || work.Plan.EntityKind != kind
            || work.Transfer.State.Artifacts is not { Count: > 0 } artifacts) return null;
        var fullPath = Path.GetFullPath(sourcePath);
        var candidates = artifacts.Where(artifact => artifact.Role == IntegrationArtifactRole.Content
            && work.Transfer.State.VerifiedArtifactIds?.Contains(artifact.Id) == true
            && FileSystemPathComparison.Equals(fullPath, Path.Combine(Path.GetFullPath(work.Plan.LibraryPath),
                IntegrationPublicationNames.FileName(id, work.Plan.Title, artifact.Id, artifact.RelativePath)))).ToArray();
        if (candidates.Length != 1) return null;
        var accepted = candidates[0];
        return await MatchesAsync(fullPath, accepted, cancellationToken) ? work.Plan.Title : null;
    }

    private async Task<string?> ResolveGalleryAsync(Guid libraryRootId, string folder, string? memberPath, CancellationToken token) {
        if (IntegrationPublicationNames.CandidateFolderOperation(folder) is not { } id) return null;
        var work = await transfers.FindAsync(id, token);
        if (work is null || work.Plan.LibraryRootId != libraryRootId || work.Plan.EntityKind != EntityKind.Gallery
            || work.Plan.Executor is not { ItemIds.Count: 1 } intent || work.Transfer.State.Artifacts is not { } artifacts) return null;
        IntegrationGalleryOutputSet outputs;
        try { outputs = new(artifacts, intent.ItemIds[0], intent.MaximumBytes); }
        catch (ArgumentException) { return null; }
        var expectedFolder = Path.Combine(Path.GetFullPath(work.Plan.LibraryPath), IntegrationGalleryNames.FolderName(id, work.Plan.Title, outputs.GroupId));
        if (!FileSystemPathComparison.Equals(Path.GetFullPath(folder), expectedFolder)
            || outputs.Artifacts.Any(artifact => work.Transfer.State.VerifiedArtifactIds?.Contains(artifact.Id) != true)) return null;
        foreach (var artifact in outputs.Artifacts) {
            var expectedPath = Path.Combine(expectedFolder, IntegrationGalleryNames.FileName(artifact));
            if (memberPath is not null && !FileSystemPathComparison.Equals(Path.GetFullPath(memberPath), expectedPath)) continue;
            if (!await MatchesAsync(expectedPath, artifact, token)) return null;
            if (memberPath is not null) return Path.GetFileNameWithoutExtension(artifact.RelativePath);
        }
        return memberPath is null ? work.Plan.Title : null;
    }

    private static async Task<bool> MatchesAsync(string path, IntegrationArtifact artifact, CancellationToken token) {
        if (!File.Exists(path)) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return file.Length == artifact.SizeBytes && Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token))
            .Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
