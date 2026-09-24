using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Recovers a finite catalog import from its last persisted boundary without repeating committed effects.</summary>
public sealed class SourceTransferProcessor(IIntegrationTransferStore store, CatalogDiscoveryService discovery,
    IntegrationConnectionAccess access, IIntegrationArtifactTransfer bytes, IIntegrationMediaVerifier verifier,
    IIntegrationImportPlacement placement, ILibraryScanRootPersistence roots, IImportedEntityMaterializer materializer) {
    private const long MaximumPublicationBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Verifies, places, and materializes exact publication bytes, persisting evidence between each recoverable boundary.</summary>
    public async Task ProcessAsync(Guid operationId, JobContext context, CancellationToken cancellationToken) {
        var work = await store.FindAsync(operationId, cancellationToken) ?? throw new IntegrationTransferNotFoundException();
        var transfer = work.Transfer;
        if (transfer.Phase.IsTerminal) return;
        if (!transfer.Mode.IsSource || work.Plan.Source is not { } source)
            throw new InvalidOperationException("This processor requires an accepted source publication.");
        var revision = transfer.State.Revision;
        try {
            VerifiedIntegrationArtifact artifact;
            LibraryRootData? root = null;
            if (transfer.State.Phase == IntegrationTransferPhase.Transferring) {
                await context.ReportProgressAsync(transfer.Mode.PreparesAtSource ? 55 : 5,
                    "Resolving selected publication", cancellationToken);
                var resolved = await discovery.ResolveSelectionAsync(transfer.State.ConnectionId, source.Selection, source.OfferId, cancellationToken);
                if (transfer.Mode.PreparesAtSource) {
                    if (source.Publication is null || source.Offer is null)
                        throw new InvalidDataException("The accepted source request is incomplete.");
                    SourceAcquisitionValidation.Validate(new(source.Selection, source.OfferId, resolved.Publication,
                        resolved.Offer, SourceAcquisitionState.Ready), source.Selection, source.OfferId, source.Publication, source.Offer);
                }
                var connection = await access.RequireAsync(transfer.State.ConnectionId, PluginCapability.AcquisitionSource,
                    IntegrationOperation.Resolve, work.Plan.EntityKind, cancellationToken);
                if (!IntegrationMediaFormats.IsSupported(work.Plan.EntityKind, resolved.Delivery.SuggestedFileName))
                    throw new InvalidDataException("The selected source changed to an unsupported publication format.");
                var origin = IntegrationDeliveryOriginPolicy.RequireAllowedOrigin(connection.Manifest.Integration, connection.Context.BaseUrl, resolved.Delivery);
                artifact = await bytes.TransferAsync(new(operationId, source.OfferId, origin,
                    resolved.Delivery, work.Plan.EntityKind == EntityKind.Image ? IntegrationMediaFormats.MaximumImageBytes : MaximumPublicationBytes), cancellationToken);
                await VerifyPublicationAsync(artifact, work.Plan.EntityKind, cancellationToken);
                transfer.AcceptSourceArtifact(new(artifact.ArtifactId, source.Selection.ItemId, artifact.FileName,
                    resolved.Offer.MediaType ?? "application/octet-stream", artifact.SizeBytes, artifact.Sha256, IntegrationArtifactRole.Content));
                await store.SaveAsync(transfer, revision, null, cancellationToken);
                revision = transfer.State.Revision;
            } else if (transfer.State.Phase == IntegrationTransferPhase.Importing && transfer.State.Artifacts is { Count: 1 } accepted) {
                var expected = accepted[0];
                root = await roots.GetLibraryRootAsync(work.Plan.LibraryRootId, cancellationToken)
                    ?? throw new InvalidDataException("The accepted destination library is unavailable.");
                artifact = await bytes.ReadVerifiedAsync(operationId, expected.Id, expected.RelativePath,
                    expected.SizeBytes, expected.Sha256, cancellationToken)
                    ?? await placement.ReadPlacedAsync(operationId, work.Plan, root, expected, cancellationToken)
                    ?? throw new InvalidDataException("Verified staging and the exact placed publication are missing.");
                await VerifyPublicationAsync(artifact, work.Plan.EntityKind, cancellationToken);
            } else throw new InvalidOperationException("This publication cannot advance from its current phase.");

            await context.ReportProgressAsync(70, "Importing verified publication", cancellationToken);
            root ??= await roots.GetLibraryRootAsync(work.Plan.LibraryRootId, cancellationToken)
                ?? throw new InvalidDataException("The accepted destination library is unavailable.");
            var path = await placement.PlaceAsync(operationId, work.Plan, root, artifact, cancellationToken);
            var imported = await materializer.MaterializeAsync(work.Plan.EntityKind, context,
                new(operationId, null, root, [path]), cancellationToken);
            await ImportedEntityReconciliation.EnqueueAsync(context, imported, null, cancellationToken);
            transfer.RecordImported(new(artifact.ArtifactId, artifact.Sha256, imported.Entities.Select(entity => entity.Id).Distinct().ToArray()));
            await store.SaveAsync(transfer, revision, null, cancellationToken);
        } catch (Exception error) when (error is not OperationCanceledException && error is not IntegrationTransferConflictException) {
            // Provider URLs, filesystem paths, and credentials must not enter public queue errors.
            var message = error is UnreadablePublicationException invalid
                ? invalid.Message
                : "Publication transfer could not finish. Verified files and accepted intent are retained; check the connection and destination, then retry.";
            await store.RecordErrorAsync(operationId, revision, message, cancellationToken);
            throw new IntegrationInvocationException(message);
        }
    }

    private async Task VerifyPublicationAsync(VerifiedIntegrationArtifact artifact, EntityKind kind, CancellationToken cancellationToken) {
        try { await verifier.VerifyAsync(artifact, kind, cancellationToken); }
        catch (InvalidDataException) {
            throw new UnreadablePublicationException(kind == EntityKind.ComicInstallment
                ? "The selected comic archive has no readable pages or could not be verified. Check the source file before retrying."
                : "The selected publication could not be verified as readable media. Check the source file before retrying.");
        }
    }

    private sealed class UnreadablePublicationException(string message) : Exception(message);
}
