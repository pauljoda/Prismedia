using System.Security.Cryptography;
using System.Text.Json;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Security;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Accepts an explicit source offer as a durable, independently retryable library import intent.</summary>
public sealed class CatalogAcquisitionService(IIntegrationTransferStore store, IDiscoveryTokenProtector tokens,
    CatalogDiscoveryService discovery, ILibraryScanRootPersistence roots, ICurrentUserContext currentUser) {
    #region Actions - Acquisition

    /// <summary>Validates the selected full publication and destination before atomically accepting its operation and
    /// queue entry.</summary>
    public async Task<IntegrationTransferResponse> AcquireAsync(Guid connectionId, AcquireCatalogOfferRequest request,
        CancellationToken cancellationToken) {
        if (request.OperationId == Guid.Empty || request.LibraryRootId == Guid.Empty || string.IsNullOrWhiteSpace(request.SelectionToken)
            || request.SelectionToken.Length > 32768 || string.IsNullOrWhiteSpace(request.OfferId) || request.OfferId.Length > 2048) {
            throw new ArgumentException("Select a publication, offer, destination library, and stable operation ID.");
        }

        var fingerprint = Fingerprint(new { connectionId, request.SelectionToken, request.OfferId, request.LibraryRootId });
        if (await store.FindAsync(request.OperationId, cancellationToken) is { } previous) {
            if (previous.Transfer.State.ConnectionId != connectionId || previous.Plan.RequestFingerprint != fingerprint) {
                throw new IntegrationTransferConflictException("This operation ID has already accepted a different request.");
            }

            return IntegrationTransferService.ToResponse(previous);
        }

        var selection = tokens.ReadSelection(connectionId, request.SelectionToken);
        if (!IntegrationImportPolicy.Supports(selection.EntityKind)
            || !IntegrationImportPolicy.For(selection.EntityKind).AcceptsDirectFiles) {
            throw new ArgumentException("Direct catalog imports support only kinds that import as one exact file.");
        }

        var import = IntegrationImportPolicy.For(selection.EntityKind);
        var allowedRoots = await currentUser.GetAllowedLibraryRootIdsAsync(cancellationToken);
        var root = await roots.GetLibraryRootAsync(request.LibraryRootId, cancellationToken);
        if (root is null || !root.Accepts(import) || allowedRoots is not null && !allowedRoots.Contains(root.Id)) {
            throw new ArgumentException("Choose an accessible, enabled library that scans this media type.");
        }

        var offer = await discovery.ResolveAsync(connectionId, request.SelectionToken, request.OfferId, cancellationToken);
        if (!import.AcceptsFileName(offer.Delivery.SuggestedFileName)) {
            throw new ArgumentException($"This format cannot be imported here. Choose a {string.Join(", ", import.Extensions)} file.");
        }

        if (offer.Delivery.ByteSize > import.MaximumBytes) {
            throw new ArgumentException($"Choose a file no larger than {import.MaximumBytes / (1024 * 1024)} MiB.");
        }

        var ownership = Fingerprint(new { connectionId, selection.ItemId, selection.EntityKind });
        var transfer = IntegrationTransfer.CreateSourceDownload(request.OperationId, connectionId);
        var plan = new IntegrationTransferPlan(offer.Publication.Title, selection.EntityKind, root.Id, Path.GetFullPath(root.Path),
            ownership, fingerprint, new(selection, request.OfferId, offer.Publication));
        return IntegrationTransferService.ToResponse(await store.CreateAsync(transfer, plan, cancellationToken));
    }

    private static string Fingerprint<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    #endregion
}
