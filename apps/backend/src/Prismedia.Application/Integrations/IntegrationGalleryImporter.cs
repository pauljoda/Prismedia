using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Imports one complete gallery and proves each member's source ownership before returning durable receipt evidence.</summary>
public sealed class IntegrationGalleryImporter(IIntegrationArtifactTransfer bytes, IIntegrationMediaVerifier verifier,
    IIntegrationGalleryPlacement placement, ILibraryScanRootPersistence roots, IImportedEntityMaterializer materializer,
    IImportedEntityReadinessPersistence readiness) {
    #region Actions - Import

    /// <summary>Replays exact group placement and materialization; no partial group can produce an acknowledgement.</summary>
    public async Task<IReadOnlyList<IntegrationArtifactImport>> ImportAsync(StoredIntegrationTransfer work, JobContext context,
        CancellationToken token) {
        var intent = work.Plan.Executor ?? throw new InvalidOperationException("A gallery requires an accepted executor selection.");
        var operationId = work.Transfer.State.OperationId;
        var outputs = new IntegrationGalleryOutputSet(work.Transfer.State.Artifacts!, intent.ItemIds.Single(), intent.MaximumBytes);
        var root = await roots.GetLibraryRootAsync(work.Plan.LibraryRootId, token)
            ?? throw new InvalidDataException("The destination library is unavailable.");
        var verified = new List<VerifiedIntegrationArtifact>();
        foreach (var artifact in outputs.Artifacts) {
            var file = await bytes.ReadVerifiedAsync(operationId, artifact.Id, Path.GetFileName(artifact.RelativePath),
                artifact.SizeBytes, artifact.Sha256, token);
            if (file is null) {
                verified.Clear();
                break;
            }

            await verifier.VerifyAsync(file, EntityKind.Image, token);
            verified.Add(file);
        }

        PlacedIntegrationGallery placed;
        if (verified.Count == outputs.Artifacts.Count) {
            placed = await placement.PlaceAsync(new(operationId, work.Plan, root, outputs, verified), token);
        } else {
            placed = await placement.ReadPlacedAsync(operationId, work.Plan, root, outputs, token)
                ?? throw new InvalidDataException("Verified gallery staging and the exact placed gallery are missing.");
            foreach (var artifact in outputs.Artifacts) {
                var recovered = new VerifiedIntegrationArtifact(artifact.Id, placed.Files[artifact.Id], artifact.SizeBytes,
                    artifact.Sha256, Path.GetFileName(artifact.RelativePath));
                await verifier.VerifyAsync(recovered, EntityKind.Image, token);
            }
        }

        var result = await materializer.MaterializeAsync(EntityKind.Gallery, context,
            new(operationId, null, root, outputs.Artifacts.Select(artifact => placed.Files[artifact.Id]).ToArray()), token);
        if (result.ProcessingRoots is not { Count: 1 } || result.ProcessingRoots[0].Kind != EntityKind.Gallery
            || result.Entities.Count != outputs.Artifacts.Count || result.Entities.Any(entity => entity.Kind != EntityKind.Image)) {
            throw new InvalidDataException("The imported gallery does not have one exact container and all its image owners.");
        }

        var galleryId = result.ProcessingRoots[0].Id;
        var owners = new HashSet<Guid>();
        var receipts = new List<IntegrationArtifactImport>();
        foreach (var artifact in outputs.Artifacts) {
            var scope = await readiness.ResolveScopeAsync([placed.Files[artifact.Id]], token);
            if (scope.Owners is not { Count: 1 } || scope.Owners[0].Kind != EntityKind.Image
                || !scope.AncestorIds.Contains(galleryId) || !result.Entities.Any(entity => entity.Id == scope.Owners[0].Id)
                || !owners.Add(scope.Owners[0].Id)) {
                throw new InvalidDataException("A gallery member does not have unique source ownership beneath the accepted container.");
            }

            receipts.Add(new(artifact.Id, artifact.Sha256, [scope.Owners[0].Id], galleryId));
        }

        await ImportedEntityReconciliation.EnqueueAsync(context, result, null, token);
        return receipts;
    }

    #endregion
}
