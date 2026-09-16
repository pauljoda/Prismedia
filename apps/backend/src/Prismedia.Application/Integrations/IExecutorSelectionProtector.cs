using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Frozen remote inspection bound to the actual persistent installation and configured connection revision.</summary>
public sealed record AcceptedExecutorSelection(string InstanceId, long ConnectionRevision, EntityKind EntityKind, TransferInspection Inspection);

/// <summary>Protects browser selections without exposing executor source URLs or accepting client-authored remote revisions.</summary>
public interface IExecutorSelectionProtector {
    /// <summary>Protects inspected evidence until its upstream expiry, subject to a host lifetime ceiling.</summary>
    string Protect(Guid connectionId, AcceptedExecutorSelection selection);
    /// <summary>Restores evidence only for its original connection and while its selection remains valid.</summary>
    AcceptedExecutorSelection Read(Guid connectionId, string token);
}
