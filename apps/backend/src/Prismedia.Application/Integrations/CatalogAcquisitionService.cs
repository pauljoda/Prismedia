using System.Security.Cryptography;
using System.Text.Json;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs;
using Prismedia.Application.Security;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Accepts an explicit source offer as a durable, independently retryable library import intent.</summary>
public sealed class CatalogAcquisitionService(IIntegrationTransferStore store, IDiscoveryTokenProtector tokens,
    CatalogDiscoveryService discovery, ILibraryScanRootPersistence roots, ICurrentUserContext currentUser, IJobQueueService queue) {
    /// <summary>Validates the selected full publication and destination before atomically accepting its operation and queue entry.</summary>
    public async Task<IntegrationTransferResponse> AcquireAsync(Guid connectionId, AcquireCatalogOfferRequest request, CancellationToken cancellationToken) {
        if (request.OperationId == Guid.Empty || request.LibraryRootId == Guid.Empty || string.IsNullOrWhiteSpace(request.SelectionToken)
            || request.SelectionToken.Length > 32768 || string.IsNullOrWhiteSpace(request.OfferId) || request.OfferId.Length > 2048)
            throw new ArgumentException("Select a publication, offer, destination library, and stable operation ID.");
        var fingerprint = Fingerprint(new { connectionId, request.SelectionToken, request.OfferId, request.LibraryRootId });
        if (await store.FindAsync(request.OperationId, cancellationToken) is { } previous) {
            if (previous.Transfer.State.ConnectionId != connectionId || previous.Plan.RequestFingerprint != fingerprint)
                throw new IntegrationTransferConflictException("This operation ID has already accepted a different request.");
            return ToResponse(previous);
        }
        var selection = tokens.ReadSelection(connectionId, request.SelectionToken);
        if (selection.EntityKind is not (EntityKind.Book or EntityKind.ComicInstallment))
            throw new ArgumentException("Direct catalog imports currently support books and comic installments.");
        var allowedRoots = await currentUser.GetAllowedLibraryRootIdsAsync(cancellationToken);
        var root = await roots.GetLibraryRootAsync(request.LibraryRootId, cancellationToken);
        if (root is null || !root.Enabled || !root.ScanBooks || allowedRoots is not null && !allowedRoots.Contains(root.Id))
            throw new ArgumentException("Choose an accessible, enabled publication library.");
        var offer = await discovery.ResolveAsync(connectionId, request.SelectionToken, request.OfferId, cancellationToken);
        if (!IntegrationPublicationFormats.IsSupported(selection.EntityKind, offer.Delivery.SuggestedFileName))
            throw new ArgumentException("This publication format cannot be imported. Choose an EPUB/PDF book or a CBZ comic offer.");
        var ownership = Fingerprint(new { connectionId, selection.ItemId, selection.EntityKind });
        var transfer = IntegrationTransfer.CreateSourceDownload(request.OperationId, connectionId);
        var plan = new IntegrationTransferPlan(offer.Publication.Title, selection.EntityKind, root.Id, Path.GetFullPath(root.Path), ownership, fingerprint,
            new(selection, request.OfferId));
        return ToResponse(await store.CreateAsync(transfer, plan, cancellationToken));
    }

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
        if (transfer.State.Mode != IntegrationTransferMode.SourceDownload
            || transfer.State.Phase is not (IntegrationTransferPhase.Transferring or IntegrationTransferPhase.Cancelled))
            throw new ArgumentException("This transfer has already begun library import and cannot be cancelled. Retry it to reconcile its files.");
        var revision = transfer.State.Revision;
        transfer.CancelSourceDownload();
        if (transfer.State.Revision != revision) await store.SaveAsync(transfer, revision, null, cancellationToken);
        await queue.CancelTargetAsync(JobType.IntegrationTransfer, id.ToString(), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private static string Fingerprint<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static IntegrationTransferResponse ToResponse(StoredIntegrationTransfer work) {
        var state = work.Transfer.State;
        return new(state.OperationId, state.ConnectionId, work.Plan.Title, work.Plan.EntityKind, work.Plan.LibraryRootId,
            state.Mode, state.Phase, work.CreatedAt, work.UpdatedAt, state.Artifacts?.Count ?? 0,
            state.Imports?.SelectMany(imported => imported.EntityIds).Distinct().ToArray() ?? [], work.LastError, work.Transfer.CanCancelSource);
    }
}

/// <summary>Formats that the existing publication scanners can safely materialize without conversion.</summary>
public static class IntegrationPublicationFormats {
    /// <summary>Whether the suggested extension is supported for this exact media kind.</summary>
    public static bool IsSupported(EntityKind kind, string fileName) => kind switch {
        EntityKind.Book => Path.GetExtension(fileName).ToLowerInvariant() is ".epub" or ".pdf",
        EntityKind.ComicInstallment => Path.GetExtension(fileName).Equals(".cbz", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
