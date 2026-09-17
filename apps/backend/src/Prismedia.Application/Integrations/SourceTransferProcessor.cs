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
        if (transfer.State.Phase is IntegrationTransferPhase.Completed or IntegrationTransferPhase.Cancelled) return;
        if (transfer.State.Mode != IntegrationTransferMode.SourceDownload || work.Plan.Source is not { } source)
            throw new InvalidOperationException("This processor requires an accepted direct-source publication.");
        var revision = transfer.State.Revision;
        try {
            VerifiedIntegrationArtifact artifact;
            if (transfer.State.Phase == IntegrationTransferPhase.Transferring) {
                await context.ReportProgressAsync(5, "Resolving selected publication", cancellationToken);
                var connection = await access.RequireAsync(transfer.State.ConnectionId, PluginCapability.AcquisitionSource,
                    IntegrationOperation.Resolve, work.Plan.EntityKind, cancellationToken);
                var resolved = await discovery.ResolveSelectionAsync(transfer.State.ConnectionId, source.Selection, source.OfferId, cancellationToken);
                if (!IntegrationMediaFormats.IsSupported(work.Plan.EntityKind, resolved.Delivery.SuggestedFileName))
                    throw new InvalidDataException("The selected source changed to an unsupported publication format.");
                artifact = await bytes.TransferAsync(new(operationId, source.OfferId, connection.Context.BaseUrl,
                    resolved.Delivery, MaximumPublicationBytes), cancellationToken);
                await verifier.VerifyAsync(artifact, work.Plan.EntityKind, cancellationToken);
                transfer.AcceptSourceArtifact(new(artifact.ArtifactId, source.Selection.ItemId, artifact.FileName,
                    resolved.Offer.MediaType ?? "application/octet-stream", artifact.SizeBytes, artifact.Sha256, IntegrationArtifactRole.Content));
                await store.SaveAsync(transfer, revision, null, cancellationToken);
                revision = transfer.State.Revision;
            } else if (transfer.State.Phase == IntegrationTransferPhase.Importing && transfer.State.Artifacts is { Count: 1 } accepted) {
                var expected = accepted[0];
                artifact = await bytes.ReadVerifiedAsync(operationId, expected.Id, expected.RelativePath,
                    expected.SizeBytes, expected.Sha256, cancellationToken)
                    ?? throw new InvalidDataException("Verified staging is missing. Restore the staged publication before retrying.");
                await verifier.VerifyAsync(artifact, work.Plan.EntityKind, cancellationToken);
            } else throw new InvalidOperationException("This publication cannot advance from its current phase.");

            await context.ReportProgressAsync(70, "Importing verified publication", cancellationToken);
            var root = await roots.GetLibraryRootAsync(work.Plan.LibraryRootId, cancellationToken)
                ?? throw new InvalidDataException("The accepted destination library is unavailable.");
            var path = await placement.PlaceAsync(operationId, work.Plan, root, artifact, cancellationToken);
            var imported = await materializer.MaterializeAsync(work.Plan.EntityKind, context,
                new(operationId, null, root, [path]), cancellationToken);
            await ImportedEntityReconciliation.EnqueueAsync(context, imported, null, cancellationToken);
            transfer.RecordImported(new(artifact.ArtifactId, artifact.Sha256, imported.Entities.Select(entity => entity.Id).Distinct().ToArray()));
            await store.SaveAsync(transfer, revision, null, cancellationToken);
        } catch (Exception error) when (error is not OperationCanceledException && error is not IntegrationTransferConflictException) {
            // Provider URLs, filesystem paths, and credentials must not enter public queue errors.
            var message = "Publication transfer could not finish. Verified files and accepted intent are retained; check the connection and destination, then retry.";
            await store.RecordErrorAsync(operationId, revision, message, cancellationToken);
            throw new IntegrationInvocationException(message);
        }
    }
}
