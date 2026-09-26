using System.Security.Cryptography;
using System.Text.Json;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Security;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Accepts one exact source-owned preparation request without performing its remote mutation on the API thread.</summary>
public sealed class SourceAcquisitionService(IIntegrationTransferStore store, IDiscoveryTokenProtector tokens,
    IntegrationConnectionAccess access, IIntegrationSourceAcquisitionGateway gateway,
    ILibraryScanRootPersistence roots, ICurrentUserContext currentUser) {
    #region Actions - Acquisition

    /// <summary>Validates a read-only observation and atomically creates the durable request and its first worker run.</summary>
    public async Task<IntegrationTransferResponse> AcquireAsync(Guid connectionId, AcquireCatalogOfferRequest request,
        CancellationToken cancellationToken) {
        if (request.OperationId == Guid.Empty || request.LibraryRootId == Guid.Empty || string.IsNullOrWhiteSpace(request.SelectionToken)
            || request.SelectionToken.Length > 32768 || string.IsNullOrWhiteSpace(request.OfferId) || request.OfferId.Length > 2048) {
            throw new ArgumentException("Select a publication, request offer, destination library, and stable operation ID.");
        }

        var fingerprint = Fingerprint(new { Mode = IntegrationTransferMode.SourceRequest, connectionId,
            request.SelectionToken, request.OfferId, request.LibraryRootId });
        if (await store.FindAsync(request.OperationId, cancellationToken) is { } previous) {
            if (previous.Transfer.State.ConnectionId != connectionId || previous.Plan.RequestFingerprint != fingerprint) {
                throw new IntegrationTransferConflictException("This operation ID has already accepted a different request.");
            }

            return IntegrationTransferService.ToResponse(previous);
        }

        var selection = tokens.ReadSelection(connectionId, request.SelectionToken);
        if (!IntegrationImportPolicy.Supports(selection.EntityKind)
            || !IntegrationImportPolicy.For(selection.EntityKind).AcceptsDirectFiles) {
            throw new ArgumentException("Source preparation supports only kinds that import as one exact file.");
        }

        var allowedRoots = await currentUser.GetAllowedLibraryRootIdsAsync(cancellationToken);
        var root = await roots.GetLibraryRootAsync(request.LibraryRootId, cancellationToken);
        if (root is null || !root.Accepts(IntegrationImportPolicy.For(selection.EntityKind))
            || allowedRoots is not null && !allowedRoots.Contains(root.Id)) {
            throw new ArgumentException("Choose an accessible, enabled library that scans this media type.");
        }

        _ = await access.RequireAsync(connectionId, PluginCapability.AcquisitionSource,
            IntegrationOperation.RequestSource, selection.EntityKind, cancellationToken);
        _ = await access.RequireAsync(connectionId, PluginCapability.AcquisitionSource,
            IntegrationOperation.Resolve, selection.EntityKind, cancellationToken);
        var authorized = await access.RequireAsync(connectionId, PluginCapability.AcquisitionSource,
            IntegrationOperation.ObserveSource, selection.EntityKind, cancellationToken);
        var observation = await gateway.ObserveSourceAsync(authorized.Manifest.Id, authorized.Context,
            new(selection, request.OfferId), cancellationToken);
        SourceAcquisitionValidation.Validate(observation, selection, request.OfferId);

        var ownership = Fingerprint(new { connectionId, selection.ItemId, selection.EntityKind });
        var transfer = IntegrationTransfer.CreateSourceRequest(request.OperationId, connectionId);
        var plan = new IntegrationTransferPlan(observation.Publication.Title, selection.EntityKind, root.Id,
            Path.GetFullPath(root.Path), ownership, fingerprint,
            new(selection, request.OfferId, observation.Publication, observation.Offer));
        return IntegrationTransferService.ToResponse(await store.CreateAsync(transfer, plan, cancellationToken));
    }

    private static string Fingerprint<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    #endregion
}
