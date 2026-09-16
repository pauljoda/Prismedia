using System.Security.Cryptography;
using System.Text.Json;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Security;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Inspects source URLs and accepts explicit publication selections before any remote job submission.</summary>
public sealed class ExecutorAcquisitionService(IntegrationConnectionAccess access, IIntegrationTransferGateway gateway,
    IExecutorSelectionProtector selections, IIntegrationTransferStore store, ILibraryScanRootPersistence roots,
    ICurrentUserContext currentUser, IntegrationTransferService status) {
    private const int MaximumInspectedItems = 100;
    /// <summary>Enforced publication budget, independent of size estimates supplied by a remote source.</summary>
    public const long MaximumPublicationBytes = 2L * 1024 * 1024 * 1024;
    private static readonly IntegrationOperation[] RequiredOperations = [IntegrationOperation.Submit, IntegrationOperation.FindSubmission,
        IntegrationOperation.GetJob, IntegrationOperation.ListArtifacts, IntegrationOperation.AuthorizeArtifact,
        IntegrationOperation.RenewRetention, IntegrationOperation.Acknowledge];

    /// <summary>Returns finite choices from an executor that provides durable identity, submission recovery, retained outputs, and receipts.</summary>
    public async Task<ExecutorInspectionResponse> InspectAsync(Guid connectionId, InspectExecutorRequest request, CancellationToken cancellationToken) {
        RequirePublicationKind(request.EntityKind);
        RequireSourceUrl(request.Url);
        var connection = await access.RequireAsync(connectionId, PluginCapability.CatalogDiscovery, IntegrationOperation.Inspect, request.EntityKind, cancellationToken);
        RequireExecutor(connection, request.EntityKind);
        var inspection = await gateway.InspectAsync(connection.Manifest.Id, connection.Context,
            new(request.Url, request.EntityKind, MaximumInspectedItems), cancellationToken);
        ValidateInspection(inspection, request.EntityKind);
        if (inspection.ExpiresAt > DateTimeOffset.UtcNow.AddHours(1)) inspection = inspection with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        var token = selections.Protect(connectionId, new(connection.Connection.State.RemoteInstanceId!, connection.Connection.State.Revision,
            request.EntityKind, inspection));
        return new(token, inspection.ExpiresAt, inspection.Items, inspection.Warnings);
    }

    /// <summary>Commits one exact inspected item and its queue entry; network ambiguity never creates another operation ID.</summary>
    public async Task<IntegrationTransferResponse> AcquireAsync(Guid connectionId, AcquireExecutorItemRequest request, CancellationToken cancellationToken) {
        if (request.OperationId == Guid.Empty || request.LibraryRootId == Guid.Empty || string.IsNullOrWhiteSpace(request.ItemId)
            || request.ItemId.Length > 2048 || string.IsNullOrWhiteSpace(request.SelectionToken) || request.SelectionToken.Length > 262144)
            throw new ArgumentException("Select an inspected publication, destination, and stable operation ID.");
        var fingerprint = Hash(new { connectionId, request.SelectionToken, request.ItemId, request.LibraryRootId });
        if (await store.FindAsync(request.OperationId, cancellationToken) is { } existing) {
            if (existing.Transfer.State.ConnectionId != connectionId || existing.Plan.RequestFingerprint != fingerprint)
                throw new IntegrationTransferConflictException("This operation ID has already accepted another selection.");
            return await status.GetAsync(request.OperationId, cancellationToken);
        }
        var selection = selections.Read(connectionId, request.SelectionToken);
        var connection = await access.RequireAsync(connectionId, PluginCapability.TransferExecutor, IntegrationOperation.Submit, selection.EntityKind, cancellationToken);
        RequireExecutor(connection, selection.EntityKind);
        if (connection.Connection.State.RemoteInstanceId != selection.InstanceId || connection.Connection.State.Revision != selection.ConnectionRevision)
            throw new ArgumentException("This connection changed after inspection. Inspect the source again before submitting.");
        ValidateInspection(selection.Inspection, selection.EntityKind);
        var item = selection.Inspection.Items.SingleOrDefault(item => item.Id == request.ItemId)
            ?? throw new ArgumentException("Choose an item from the inspected selection.");
        var allowed = await currentUser.GetAllowedLibraryRootIdsAsync(cancellationToken);
        var root = await roots.GetLibraryRootAsync(request.LibraryRootId, cancellationToken);
        if (root is null || !root.Enabled || !root.ScanBooks || allowed is not null && !allowed.Contains(root.Id))
            throw new ArgumentException("Choose an accessible, enabled publication library.");
        var intent = new SubmitTransferInput(request.OperationId, selection.Inspection.CanonicalUrl, selection.Inspection.SelectionId,
            selection.Inspection.Revision, [item.Id], 1, MaximumPublicationBytes);
        var plan = new IntegrationTransferPlan(item.Title, selection.EntityKind, root.Id, Path.GetFullPath(root.Path),
            Hash(new { connectionId, ItemId = item.Id, selection.EntityKind }), fingerprint, Executor: intent);
        await store.CreateAsync(IntegrationTransfer.Create(request.OperationId, connectionId, selection.InstanceId), plan, cancellationToken);
        return await status.GetAsync(request.OperationId, cancellationToken);
    }

    private static void RequireExecutor(AuthorizedIntegrationConnection connection, EntityKind kind) {
        if (!connection.Connection.State.HasPersistentRemoteIdentity || string.IsNullOrWhiteSpace(connection.Connection.State.RemoteInstanceId)
            || RequiredOperations.Any(operation => !connection.Connection.Allows(PluginCapability.TransferExecutor, operation, kind)
                || connection.Manifest.Integration?.Capabilities.Any(capability => capability.Kind == PluginCapability.TransferExecutor
                    && capability.Operations.Contains(operation) && capability.EntityKinds.Contains(kind)) != true))
            throw new ArgumentException("This executor must support persistent identity, submission recovery, retained artifacts, and import receipts.");
    }
    private static void ValidateInspection(TransferInspection inspection, EntityKind kind) {
        RequirePublicationKind(kind);
        if (inspection is null || string.IsNullOrWhiteSpace(inspection.SelectionId) || inspection.SelectionId.Length > 512
            || string.IsNullOrWhiteSpace(inspection.Revision) || inspection.Revision.Length > 512 || inspection.ExpiresAt <= DateTimeOffset.UtcNow
            || inspection.Items is not { Count: > 0 and <= MaximumInspectedItems } || inspection.Items.Any(item => item is null)
            || inspection.Items.Select(item => item.Id).Distinct().Count() != inspection.Items.Count
            || inspection.Items.Any(item => string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 2048 || string.IsNullOrWhiteSpace(item.Title)
                || item.Title.Length > 512 || item.EntityKind != kind)
            || inspection.Warnings is null || inspection.Warnings.Count > 20 || inspection.Warnings.Any(message => message is null || message.Length > 1024))
            throw new IntegrationInvocationException("The executor returned an invalid, expired, or unbounded publication selection.");
        RequireSourceUrl(inspection.CanonicalUrl);
    }
    private static void RequirePublicationKind(EntityKind kind) {
        if (kind is not (EntityKind.Book or EntityKind.ComicInstallment)) throw new ArgumentException("Choose a book or comic installment for publication acquisition.");
    }
    private static void RequireSourceUrl(string url) {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 8192 || !Uri.TryCreate(url, UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https") || address.UserInfo.Length != 0 || address.Fragment.Length != 0)
            throw new ArgumentException("Use an HTTP or HTTPS source URL without embedded login credentials or a fragment.");
    }
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
