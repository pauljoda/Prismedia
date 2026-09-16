using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Resumes durable executor fulfillment across submission ambiguity, remote waits, byte transfer, local import, and receipt delivery.</summary>
public sealed class RemoteTransferProcessor(IIntegrationTransferStore store, IntegrationConnectionAccess access,
    IIntegrationTransferGateway gateway, IntegrationManifestReader manifests, IIntegrationArtifactTransfer bytes,
    IIntegrationPublicationVerifier verifier, IIntegrationImportPlacement placement, ILibraryScanRootPersistence roots,
    IImportedEntityMaterializer materializer) {
    /// <summary>Runs until a durable remote wait or local completion boundary. A wait releases the queue worker without consuming a failure attempt.</summary>
    public async Task ProcessAsync(Guid operationId, JobContext context, CancellationToken cancellationToken) {
        var work = await store.FindAsync(operationId, cancellationToken) ?? throw new IntegrationTransferNotFoundException();
        var transfer = work.Transfer;
        if (transfer.State.Mode != IntegrationTransferMode.RemoteExecutor || work.Plan.Executor is not { } intent)
            throw new InvalidOperationException("This processor requires a durable executor intent.");
        if (transfer.State.Phase is IntegrationTransferPhase.Completed or IntegrationTransferPhase.Cancelled or IntegrationTransferPhase.Failed) return;
        var persistedRevision = transfer.State.Revision;
        async Task PersistAsync(string? message = null) {
            if (transfer.State.Revision == persistedRevision) {
                if (message is not null) await store.RecordErrorAsync(operationId, persistedRevision, message, cancellationToken);
                return;
            }
            await store.SaveAsync(transfer, persistedRevision, message, cancellationToken);
            persistedRevision = transfer.State.Revision;
        }
        async Task<AuthorizedIntegrationConnection> AuthorizeAsync(IntegrationOperation operation) {
            var connection = await access.RequireAsync(transfer.State.ConnectionId, PluginCapability.TransferExecutor,
                operation, work.Plan.EntityKind, cancellationToken);
            if (!connection.Connection.State.HasPersistentRemoteIdentity || connection.Context.ExpectedInstanceId != transfer.State.InstanceId)
                throw new IntegrationInvocationException("The executor installation differs from the accepted operation.");
            return connection;
        }
        async Task RenewAsync() {
            var connection = await AuthorizeAsync(IntegrationOperation.RenewRetention);
            var until = DateTimeOffset.UtcNow.AddHours(1);
            var result = await gateway.RenewRetentionAsync(connection.Manifest.Id, connection.Context, new(transfer.State.JobId!, until), cancellationToken);
            if (result.JobId != transfer.State.JobId || result.RetainedUntil < until)
                throw new IntegrationInvocationException("The executor did not guarantee the requested output retention.");
        }
        void ValidateSnapshot(RemoteTransferSnapshot snapshot) {
            if (snapshot is null || snapshot.ClientOperationId != operationId || snapshot.InstanceId != transfer.State.InstanceId
                || string.IsNullOrWhiteSpace(snapshot.JobId) || snapshot.JobId.Length > 512
                || transfer.State.JobId is not null && snapshot.JobId != transfer.State.JobId || snapshot.Revision < 0
                || !Enum.IsDefined(snapshot.State) || snapshot.Progress is { } progress && (!double.IsFinite(progress) || progress is < 0 or > 1)
                || snapshot.ItemFailures is null || snapshot.ItemFailures.Count > intent.MaximumItems
                || snapshot.ItemFailures.Any(failure => failure is null || !intent.ItemIds.Contains(failure.ItemId)
                    || failure.Message is null || failure.Message.Length > 4096)
                || snapshot.State == RemoteJobState.Succeeded && snapshot.ItemFailures.Count != 0)
                throw new IntegrationInvocationException("The executor returned an invalid or unrelated job snapshot.");
        }
        try {
            RemoteTransferSnapshot? snapshot = null;
            if (transfer.State.Phase == IntegrationTransferPhase.PendingSubmission) {
                transfer.BeginSubmission();
                await PersistAsync(); // A crash after this point must recover by operation ID before another POST.
            }
            if (transfer.State.Phase == IntegrationTransferPhase.SubmissionUncertain) {
                var lookup = await AuthorizeAsync(IntegrationOperation.FindSubmission);
                var found = await gateway.FindSubmissionAsync(lookup.Manifest.Id, lookup.Context, new(operationId), cancellationToken);
                snapshot = found.Job;
                if (snapshot is null) {
                    var submit = await AuthorizeAsync(IntegrationOperation.Submit);
                    snapshot = await gateway.SubmitAsync(submit.Manifest.Id, submit.Context, intent, cancellationToken);
                }
                ValidateSnapshot(snapshot);
                transfer.AcceptSubmission(snapshot.ClientOperationId, snapshot.InstanceId, snapshot.JobId);
                await PersistAsync();
            }
            if (transfer.State.Phase is IntegrationTransferPhase.AwaitingRemote or IntegrationTransferPhase.NeedsReview) {
                if (snapshot is null) {
                    var connection = await AuthorizeAsync(IntegrationOperation.GetJob);
                    snapshot = await gateway.GetJobAsync(connection.Manifest.Id, connection.Context, new(transfer.State.JobId!), cancellationToken);
                }
                ValidateSnapshot(snapshot);
                transfer.Observe(snapshot.InstanceId, snapshot.JobId, snapshot.Revision, snapshot.State, snapshot.ManifestRevision);
                await PersistAsync();
                if (transfer.State.Phase == IntegrationTransferPhase.AwaitingRemote)
                    throw new JobRetryLaterException("Waiting for the executor to finish the accepted publication.", PollDelay(snapshot.NextPollAfter));
                if (snapshot.State is RemoteJobState.Failed or RemoteJobState.Cancelled) return;
                if (snapshot.State == RemoteJobState.Partial) {
                    await RenewAsync();
                    await PersistAsync("The executor produced only part of the selected publication. No partial content was imported.");
                    throw new JobRetryLaterException("Partial outputs require review; preserving the executor's retention lease.", TimeSpan.FromMinutes(5));
                }
                if (snapshot.ArtifactsExpired) {
                    if (transfer.State.Phase == IntegrationTransferPhase.AwaitingArtifacts) transfer.HoldUnavailableRemoteOutputs();
                    await PersistAsync("The executor's outputs expired before local verification. Restore those exact outputs before retrying.");
                    throw new JobRetryLaterException("Waiting for the executor to restore its retained outputs.", TimeSpan.FromMinutes(5));
                }
            }
            if (transfer.State.Phase is IntegrationTransferPhase.AwaitingArtifacts or IntegrationTransferPhase.NeedsReview) {
                await RenewAsync();
                var connection = await AuthorizeAsync(IntegrationOperation.ListArtifacts);
                var manifest = await manifests.ReadAsync(connection.Manifest.Id, connection.Context, transfer.State.JobId!, transfer.State.ManifestRevision!, cancellationToken);
                if (!SupportsPublication(manifest.Artifacts, work.Plan, intent)) {
                    if (transfer.State.Phase == IntegrationTransferPhase.AwaitingArtifacts) transfer.HoldUnavailableRemoteOutputs();
                    await PersistAsync("This publication profile requires one complete EPUB/PDF book or CBZ comic. Additional outputs are retained by the executor for review.");
                    throw new JobRetryLaterException("Outputs require review; preserving the executor's retention lease.", TimeSpan.FromMinutes(5));
                }
                transfer.AcceptManifest(manifest);
                await PersistAsync();
            }
            if (transfer.State.Phase == IntegrationTransferPhase.Transferring) {
                foreach (var artifact in transfer.State.Artifacts!) {
                    if (transfer.State.VerifiedArtifactIds?.Contains(artifact.Id) == true) continue;
                    await RenewAsync();
                    var connection = await AuthorizeAsync(IntegrationOperation.AuthorizeArtifact);
                    var delivery = await gateway.AuthorizeArtifactAsync(connection.Manifest.Id, connection.Context,
                        new(transfer.State.JobId!, transfer.State.ManifestRevision!, artifact.Id), cancellationToken);
                    if (delivery.ByteSize != artifact.SizeBytes || !string.Equals(delivery.Sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IntegrationInvocationException("Artifact delivery differs from the accepted output manifest.");
                    var verified = await bytes.TransferAsync(new(operationId, artifact.Id, connection.Context.BaseUrl,
                        delivery with { SuggestedFileName = Path.GetFileName(artifact.RelativePath) }, intent.MaximumBytes), cancellationToken);
                    await verifier.VerifyAsync(verified, work.Plan.EntityKind, cancellationToken);
                    transfer.RecordVerified(artifact.Id, verified.SizeBytes, verified.Sha256);
                    await PersistAsync();
                }
            }
            if (transfer.State.Phase == IntegrationTransferPhase.Importing) {
                foreach (var artifact in transfer.State.Artifacts!) {
                    if (transfer.State.Imports?.Any(imported => imported.ArtifactId == artifact.Id) == true) continue;
                    var verified = await bytes.ReadVerifiedAsync(operationId, artifact.Id, Path.GetFileName(artifact.RelativePath), artifact.SizeBytes, artifact.Sha256, cancellationToken)
                        ?? throw new InvalidDataException("Verified staging is missing.");
                    await verifier.VerifyAsync(verified, work.Plan.EntityKind, cancellationToken);
                    var root = await roots.GetLibraryRootAsync(work.Plan.LibraryRootId, cancellationToken)
                        ?? throw new InvalidDataException("The destination library is unavailable.");
                    await context.ReportProgressAsync(75, "Importing verified executor output", cancellationToken);
                    var path = await placement.PlaceAsync(operationId, work.Plan, root, verified, cancellationToken);
                    var imported = await materializer.MaterializeAsync(work.Plan.EntityKind, context, new(operationId, null, root, [path]), cancellationToken);
                    await ImportedEntityReconciliation.EnqueueAsync(context, imported, null, cancellationToken);
                    transfer.RecordImported(new(artifact.Id, artifact.Sha256, imported.Entities.Select(entity => entity.Id).Distinct().ToArray()));
                    await PersistAsync(); // Receipt identity and exact local ownership must commit before acknowledgement.
                }
            }
            if (transfer.State.Phase == IntegrationTransferPhase.AwaitingAcknowledgement) {
                var connection = await AuthorizeAsync(IntegrationOperation.Acknowledge);
                var receiptId = transfer.State.ReceiptId!.Value;
                var acknowledgement = await gateway.AcknowledgeAsync(connection.Manifest.Id, connection.Context,
                    new(transfer.State.JobId!, transfer.State.ManifestRevision!, receiptId, transfer.State.Imports!), cancellationToken);
                if (!acknowledgement.Accepted || acknowledgement.JobId != transfer.State.JobId || acknowledgement.ReceiptId != receiptId)
                    throw new IntegrationInvocationException("The executor did not confirm the exact import receipt.");
                transfer.RecordAcknowledgement(receiptId);
                await PersistAsync();
            }
        } catch (JobRetryLaterException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (IntegrationTransferConflictException) { throw; }
        catch (IntegrationInvocationException) {
            const string message = "The executor could not confirm this step. The accepted operation and local evidence are retained; reconciliation will retry.";
            await store.RecordErrorAsync(operationId, persistedRevision, message, cancellationToken);
            throw new JobRetryLaterException(message, TimeSpan.FromMinutes(1));
        } catch (Exception) {
            const string message = "Executor import needs attention. Accepted outputs and verified files are retained; check the connection and destination before retrying.";
            await store.RecordErrorAsync(operationId, persistedRevision, message, cancellationToken);
            throw new IntegrationInvocationException(message);
        }
    }

    private static bool SupportsPublication(IReadOnlyList<IntegrationArtifact> artifacts, IntegrationTransferPlan plan, SubmitTransferInput intent) =>
        intent.ItemIds.Count == 1 && artifacts.Count == 1 && artifacts[0].Role == IntegrationArtifactRole.Content
        && artifacts[0].ItemId == intent.ItemIds[0] && artifacts[0].SizeBytes <= intent.MaximumBytes
        && IntegrationPublicationFormats.IsSupported(plan.EntityKind, artifacts[0].RelativePath);

    private static TimeSpan PollDelay(DateTimeOffset? nextPollAfter) =>
        TimeSpan.FromSeconds(Math.Clamp((nextPollAfter - DateTimeOffset.UtcNow)?.TotalSeconds ?? 15, 5, 3600));
}
