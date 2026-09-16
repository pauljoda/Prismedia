using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Narrow executor operations. Application handlers authorize each operation and persist side-effect intent before calling this port.</summary>
public interface IIntegrationTransferGateway {
    /// <summary>Inspects URL support and produces a bounded, revisioned selection without creating a job.</summary>
    Task<TransferInspection> InspectAsync(string pluginId, IntegrationConnectionContext connection, InspectTransferInput input, CancellationToken cancellationToken);
    /// <summary>Submits a previously persisted operation key; repeat requests must recover the same job.</summary>
    Task<RemoteTransferSnapshot> SubmitAsync(string pluginId, IntegrationConnectionContext connection, SubmitTransferInput input, CancellationToken cancellationToken);
    /// <summary>Recovers an ambiguous submission before another attempt is considered.</summary>
    Task<FindTransferResult> FindSubmissionAsync(string pluginId, IntegrationConnectionContext connection, FindTransferInput input, CancellationToken cancellationToken);
    /// <summary>Cancels by durable operation identity, atomically preventing a late POST if no job has been accepted yet.</summary>
    Task<CancelSubmissionResult> CancelSubmissionAsync(string pluginId, IntegrationConnectionContext connection, FindTransferInput input, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This gateway does not support atomic operation cancellation.");
    /// <summary>Reads durable job state even after the executor removes it from its active queue.</summary>
    Task<RemoteTransferSnapshot> GetJobAsync(string pluginId, IntegrationConnectionContext connection, RemoteTransferJobInput input, CancellationToken cancellationToken);
    /// <summary>Requests cancellation; the returned snapshot must establish the actual outcome.</summary>
    Task<RemoteTransferSnapshot> CancelAsync(string pluginId, IntegrationConnectionContext connection, RemoteTransferJobInput input, CancellationToken cancellationToken);
    /// <summary>Reads one stable page of a sealed output revision.</summary>
    Task<TransferManifestPage> ReadManifestAsync(string pluginId, IntegrationConnectionContext connection, ReadTransferManifestInput input, CancellationToken cancellationToken);
    /// <summary>Obtains server-only HTTP byte authorization for one frozen artifact.</summary>
    Task<HttpArtifactDelivery> AuthorizeArtifactAsync(string pluginId, IntegrationConnectionContext connection, AuthorizeTransferArtifactInput input, CancellationToken cancellationToken);
    /// <summary>Renews the retention guarantee; refusal is not permission to clean up local recovery evidence.</summary>
    Task<TransferRetentionResult> RenewRetentionAsync(string pluginId, IntegrationConnectionContext connection, RenewTransferRetentionInput input, CancellationToken cancellationToken);
    /// <summary>Records an already committed import receipt without repeating download or import.</summary>
    Task<TransferAcknowledgement> AcknowledgeAsync(string pluginId, IntegrationConnectionContext connection, AcknowledgeTransferInput input, CancellationToken cancellationToken);
}
