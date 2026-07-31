using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class IdentifyQueueService {
    /// <summary>Closes the review wait and queues proposal application in the item's graph.</summary>
    public async Task<IdentifyQueueItem> ApplyAsync(
        Guid entityId,
        ApplyIdentifyQueueItemRequest request,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        (await _eligibility.EvaluateAsync(entityId, cancellationToken)).EnsureEligible();

        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Identify queue item for entity '{entityId}' was not found.");

        if (row.State == IdentifyQueueState.Applying) {
            return await MapRowAsync(row, cancellationToken);
        }

        // Terminal rows are one-way. A Done/Deleted item still carries its ProposalJson (kept for
        // history), so without this guard a re-POST, double-click, or a bulk-accept loop hitting the same
        // entity twice would re-run the full recursive write. Reject instead of silently re-applying.
        if (row.State is IdentifyQueueState.Done or IdentifyQueueState.Deleted) {
            throw new InvalidOperationException(
                $"Identify queue item for entity '{entityId}' is already '{row.State.ToCode()}' and cannot be applied again.");
        }

        // A queued or running search means the stored ProposalJson is stale (the request cleared it,
        // or a new result is about to land): applying now would write the wrong metadata.
        if (row.State is IdentifyQueueState.Queued or IdentifyQueueState.Searching) {
            throw new InvalidOperationException(
                $"Identify queue item for entity '{entityId}' is awaiting its requested search; cannot apply yet.");
        }

        // Do not apply while the background cascade is still streaming the child tree: the stored proposal
        // is only partial until the cascade clears its marker, so applying now would drop the children
        // that have not streamed in yet. The single-item review disables Accept on this same signal;
        // enforce it here too so the bulk-accept path cannot apply a half-resolved tree.
        if (row.CascadeJobId is not null) {
            throw new InvalidOperationException(
                $"Identify cascade for entity '{entityId}' is still resolving children; cannot apply yet.");
        }

        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var storedProposal = Deserialize<EntityMetadataProposal>(row.ProposalJson)
            ?? throw new InvalidOperationException("Identify queue item has no proposal to apply.");
        var proposal = request.Proposal ?? storedProposal;
        if (!string.Equals(proposal.ProposalId, storedProposal.ProposalId, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Only the root identify proposal can be applied to a queue item.");
        }
        if (proposal.TargetKind != entity.KindCode.DecodeAs<EntityKind>()) {
            throw new InvalidOperationException("Identify proposal target kind does not match the queued entity.");
        }

        var payload = new IdentifyApplyPayload(entityId, request);
        var node = new GraphJobNodeRequest(
            $"identify-apply:{entityId}",
            new EnqueueJobRequest(
                JobType.IdentifyApply,
                payload.ToJson(),
                TargetEntityKind: entity.KindCode,
                TargetEntityId: entityId.ToString(),
                TargetLabel: entity.Title,
                Origin: JobGraphOrigin.Interactive),
            Importance: JobNodeImportance.Required,
            ResourceClass: JobResourceClass.Light,
            ResourceKey: JobResourceKeys.Entity(entityId.ToString()));

        row.State = IdentifyQueueState.Applying;
        row.Error = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.CompletedAt = null;
        await _db.SaveChangesAsync(cancellationToken);

        // Compatibility for isolated hosts and focused tests that do not register graph services.
        // The production API always supplies IJobGraphService and therefore always takes the durable path.
        if (_graphs is null) {
            await RunApplyAsync(payload, row.JobGraphId ?? Guid.Empty, isFinalAttempt: true, cancellationToken);
            return await MapRowAsync(row, cancellationToken);
        }

        if (row.JobGraphId is { } graphId) {
            var reviewKey = IdentifyGraphSignals.Review(entityId);
            var graph = await _graphs.GetAsync(graphId, cancellationToken);
            if (graph?.Signals.Any(signal =>
                    signal.Key == reviewKey && signal.ResolvedAt is null && signal.CancelledAt is null) == true) {
                await _graphs.ResolveSignalAsync(graphId, reviewKey, [node], cancellationToken);
            } else if (graph is not null && graph.Graph.Status is not (
                JobGraphStatus.Completed or JobGraphStatus.CompletedWithWarnings or
                JobGraphStatus.Failed or JobGraphStatus.Cancelled)) {
                await _graphs.AppendNodeAsync(graphId, node, cancellationToken);
            } else {
                var root = await _jobs.EnqueueAsync(node.Job, cancellationToken);
                row.JobGraphId = root.GraphId;
                await _db.SaveChangesAsync(cancellationToken);
            }
        } else {
            var root = await _jobs.EnqueueAsync(node.Job, cancellationToken);
            row.JobGraphId = root.GraphId;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return MapRow(row, entity);
    }

    /// <summary>Applies one graph-owned reviewed proposal and marks its queue item done.</summary>
    public async Task RunApplyAsync(
        IdentifyApplyPayload payload,
        Guid graphId,
        bool isFinalAttempt,
        CancellationToken cancellationToken) {
        var entityId = payload.EntityId;
        var request = payload.Request;
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        var ownsGraph = row?.JobGraphId == graphId || row?.JobGraphId is null && graphId == Guid.Empty;
        if (row is null || !ownsGraph || row.State != IdentifyQueueState.Applying) {
            return;
        }

        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var storedProposal = Deserialize<EntityMetadataProposal>(row.ProposalJson)
            ?? throw new InvalidOperationException("Identify queue item has no proposal to apply.");
        var proposal = request.Proposal ?? storedProposal;
        if (!string.Equals(proposal.ProposalId, storedProposal.ProposalId, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Only the root identify proposal can be applied to a queue item.");
        }
        if (proposal.TargetKind != entity.KindCode.DecodeAs<EntityKind>()) {
            throw new InvalidOperationException("Identify proposal target kind does not match the queued entity.");
        }
        var preparedProposal = await _identify.PrepareApplyProposalAsync(
            entityId,
            proposal,
            cancellationToken);
        var acceptedProposal = AcceptedProposalMarker.MarkTreeOrganized(preparedProposal);
        IdentifyApplyProgressReporter? progressReporter = null;
        if (request.ProgressId is { } progressId) {
            _progress.Begin(progressId, entityId, CountApplySteps(acceptedProposal, request.SelectedFields));
            progressReporter = new IdentifyApplyProgressReporter(_progress, progressId);
        }

        try {
            var applied = await _identify.ApplyPreparedProposalAsync(
                entityId,
                acceptedProposal,
                request.SelectedFields,
                request.SelectedImages,
                progressReporter,
                cancellationToken);
            if (!applied) {
                throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            row.State = IdentifyQueueState.Done;
            row.ProposalJson = JsonSerializer.Serialize(acceptedProposal, JsonOptions);
            row.Error = null;
            row.UpdatedAt = now;
            row.CompletedAt = now;

            var entityRow = await _db.Entities.FindAsync([entityId], cancellationToken);
            if (entityRow is not null) {
                entityRow.IsOrganized = true;
                entityRow.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            if (_graphs is not null && row.JobGraphId is { } owningGraphId) {
                var reconcile = new GraphJobNodeRequest(
                    $"reconcile-entity:{entityId}",
                    new EnqueueJobRequest(
                        JobType.ReconcileEntity,
                        TargetEntityKind: entity.KindCode,
                        TargetEntityId: entityId.ToString(),
                        TargetLabel: entity.Title),
                    Importance: JobNodeImportance.BestEffort,
                    ResourceClass: JobResourceClass.Light,
                    ResourceKey: JobResourceKeys.Entity(entityId.ToString()));
                var graph = await _graphs.GetAsync(owningGraphId, cancellationToken);
                if (graph is not null && graph.Graph.Status is not (
                    JobGraphStatus.Completed or JobGraphStatus.CompletedWithWarnings or JobGraphStatus.Failed or JobGraphStatus.Cancelled)) {
                    await _graphs.AppendNodeAsync(owningGraphId, reconcile, cancellationToken);
                }
            }
            if (request.ProgressId is { } completedProgressId) {
                _progress.Complete(completedProgressId);
            }
        } catch (Exception ex) {
            if (request.ProgressId is { } failedProgressId) {
                _progress.Fail(failedProgressId, ex.Message);
            }
            var stillOwnsGraph = row.JobGraphId == graphId || row.JobGraphId is null && graphId == Guid.Empty;
            if (isFinalAttempt && stillOwnsGraph && row.State == IdentifyQueueState.Applying) {
                row.State = IdentifyQueueState.Error;
                row.Error = ex.Message;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            throw;
        }

        var refreshedEntity = await LoadEntityAsync(entityId, cancellationToken) ?? entity;
        await QueueMonitoredRefreshAsync(
            entityId,
            refreshedEntity.Title,
            cancellationToken);
    }

    /// <summary>
    /// Persists an updated in-progress proposal onto the queued entity (e.g. as children resolve)
    /// without applying it, so the accumulated proposal survives navigation and page refresh.
    /// </summary>
    public async Task<IdentifyQueueItem> SaveProposalAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(proposal);

        (await _eligibility.EvaluateAsync(entityId, cancellationToken)).EnsureEligible();

        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Identify queue item for entity '{entityId}' was not found.");
        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var storedProposal = Deserialize<EntityMetadataProposal>(row.ProposalJson)
            ?? throw new InvalidOperationException("Identify queue item has no proposal to update.");
        if (!string.Equals(proposal.ProposalId, storedProposal.ProposalId, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Only the queue item's own root proposal can be saved.");
        }

        row.ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    /// <summary>
    /// Removes an item from the active identify queue without applying metadata.
    /// </summary>
    public async Task<IdentifyQueueItem?> DeleteAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is null) {
            return null;
        }

        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        await RetireRowAsync(row, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (_graphs is not null && row.JobGraphId is { } graphId) {
            await _graphs.CancelAsync(graphId, cancellationToken);
        }
        return MapRow(row, entity);
    }

}
