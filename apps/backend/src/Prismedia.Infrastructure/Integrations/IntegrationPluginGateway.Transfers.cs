using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Typed executor operations over the shared bounded native transport.</summary>
public sealed partial class IntegrationPluginGateway {
    #region Actions - Transfers

    /// <inheritdoc />
    public Task<TransferInspection> InspectAsync(string pluginId, IntegrationConnectionContext connection,
        InspectTransferInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<InspectTransferInput, TransferInspection>(pluginId, IntegrationOperation.Inspect,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<RemoteTransferSnapshot> SubmitAsync(string pluginId, IntegrationConnectionContext connection,
        SubmitTransferInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<SubmitTransferInput, RemoteTransferSnapshot>(pluginId, IntegrationOperation.Submit,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<FindTransferResult> FindSubmissionAsync(string pluginId, IntegrationConnectionContext connection,
        FindTransferInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<FindTransferInput, FindTransferResult>(pluginId, IntegrationOperation.FindSubmission,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<CancelSubmissionResult> CancelSubmissionAsync(string pluginId, IntegrationConnectionContext connection,
        FindTransferInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<FindTransferInput, CancelSubmissionResult>(pluginId, IntegrationOperation.CancelSubmission,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<RemoteTransferSnapshot> GetJobAsync(string pluginId, IntegrationConnectionContext connection,
        RemoteTransferJobInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<RemoteTransferJobInput, RemoteTransferSnapshot>(pluginId, IntegrationOperation.GetJob,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<RemoteTransferSnapshot> CancelAsync(string pluginId, IntegrationConnectionContext connection,
        RemoteTransferJobInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<RemoteTransferJobInput, RemoteTransferSnapshot>(pluginId, IntegrationOperation.Cancel,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<TransferManifestPage> ReadManifestAsync(string pluginId, IntegrationConnectionContext connection,
        ReadTransferManifestInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<ReadTransferManifestInput, TransferManifestPage>(pluginId, IntegrationOperation.ListArtifacts,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<HttpArtifactDelivery> AuthorizeArtifactAsync(string pluginId, IntegrationConnectionContext connection,
        AuthorizeTransferArtifactInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<AuthorizeTransferArtifactInput, HttpArtifactDelivery>(pluginId, IntegrationOperation.AuthorizeArtifact,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<TransferRetentionResult> RenewRetentionAsync(string pluginId, IntegrationConnectionContext connection,
        RenewTransferRetentionInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<RenewTransferRetentionInput, TransferRetentionResult>(pluginId, IntegrationOperation.RenewRetention,
            connection, input, cancellationToken);

    /// <inheritdoc />
    public Task<TransferAcknowledgement> AcknowledgeAsync(string pluginId, IntegrationConnectionContext connection,
        AcknowledgeTransferInput input, CancellationToken cancellationToken) =>
        TransferInvokeAsync<AcknowledgeTransferInput, TransferAcknowledgement>(pluginId, IntegrationOperation.Acknowledge,
            connection, input, cancellationToken);

    #endregion

    #region Actions - Invocation

    private async Task<TOutput> TransferInvokeAsync<TInput, TOutput>(string pluginId, IntegrationOperation operation,
        IntegrationConnectionContext connection, TInput input, CancellationToken cancellationToken) where TOutput : class {
        var descriptor = await FindDescriptorAsync(pluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        return await InvokeAsync<TInput, TOutput>(descriptor, operation, connection, input, cancellationToken);
    }

    #endregion
}
