using Prismedia.Application.Jobs;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Reads durable transfer progress and coordinates explicit recovery independently of its acquisition source.</summary>
public sealed class IntegrationTransferService(IIntegrationTransferStore store, IJobQueueService queue) {
    /// <summary>Lists recent persisted transfers for administrator status views.</summary>
    public async Task<IReadOnlyList<IntegrationTransferResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(100, cancellationToken)).Select(ToResponse).ToArray();

    /// <summary>Retrieves durable progress even after a queue run has been pruned.</summary>
    public async Task<IntegrationTransferResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await store.FindAsync(id, cancellationToken) ?? throw new IntegrationTransferNotFoundException());

    /// <summary>Retries unfinished work using its original operation, source selection, and byte/import evidence.</summary>
    public async Task RetryAsync(Guid id, CancellationToken cancellationToken) => await store.EnqueueRetryAsync(id, cancellationToken);

    /// <summary>Cancels before library placement begins. The saved revision fences an in-flight worker before cancelling its run.</summary>
    public async Task<IntegrationTransferResponse> CancelAsync(Guid id, CancellationToken cancellationToken) {
        var work = await store.FindAsync(id, cancellationToken) ?? throw new IntegrationTransferNotFoundException();
        var transfer = work.Transfer;
        if (!transfer.Mode.IsSource) {
            if (!transfer.CanCancelRemote && !transfer.State.CancellationRequested && transfer.State.Phase != IntegrationTransferPhase.Cancelled)
                throw new ArgumentException("Local import has started or this operation is already terminal. Reconcile its existing files before requesting another copy.");
            var expected = transfer.State.Revision;
            transfer.RequestRemoteCancellation();
            if (transfer.State.Revision != expected) await store.SaveAndEnqueueAsync(transfer, expected, cancellationToken);
            else if (transfer.State.Phase != IntegrationTransferPhase.Cancelled) await store.EnqueueRetryAsync(id, cancellationToken);
            return await GetAsync(id, cancellationToken);
        }
        if (!transfer.CanCancelSource && transfer.State.Phase != IntegrationTransferPhase.Cancelled)
            throw new ArgumentException("This transfer has already begun library import and cannot be cancelled. Retry it to reconcile its files.");
        var revision = transfer.State.Revision;
        transfer.CancelSourceDownload();
        if (transfer.State.Revision != revision) await store.SaveAsync(transfer, revision, null, cancellationToken);
        await queue.CancelTargetAsync(JobType.IntegrationTransfer, id.ToString(), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    internal static IntegrationTransferResponse ToResponse(StoredIntegrationTransfer work) {
        var state = work.Transfer.State;
        return new(state.OperationId, state.ConnectionId, work.Plan.Title, work.Plan.EntityKind, work.Plan.LibraryRootId,
            state.Mode, state.Phase, work.CreatedAt, work.UpdatedAt, state.Artifacts?.Count ?? 0,
            state.Imports?.SelectMany(imported => imported.ContainerEntityId is { } container ? new[] { container } : imported.EntityIds).Distinct().ToArray() ?? [], work.LastError,
            work.Transfer.CanCancelSource || work.Transfer.CanCancelRemote, state.CancellationRequested, work.Plan.Source?.Publication,
            state.LastSourceState, state.SourceProgress, state.SourceProblem);
    }
}
