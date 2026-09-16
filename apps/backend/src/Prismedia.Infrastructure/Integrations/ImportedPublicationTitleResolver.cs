using System.Security.Cryptography;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Uses the existing durable import journal, exact accepted path, and verified bytes rather than heuristically stripping filename suffixes.</summary>
public sealed class ImportedPublicationTitleResolver(IIntegrationTransferStore transfers) : IImportedPublicationTitleResolver {
    /// <inheritdoc />
    public async Task<string?> ResolveAsync(Guid libraryRootId, EntityKind kind, string sourcePath, CancellationToken cancellationToken) {
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
        await using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        if (file.Length != accepted.SizeBytes) return null;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
        return hash.Equals(accepted.Sha256, StringComparison.OrdinalIgnoreCase) ? work.Plan.Title : null;
    }
}
