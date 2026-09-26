using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Frozen remote inspection bound to the actual persistent installation and configured connection revision.</summary>
public sealed record AcceptedExecutorSelection(string InstanceId, long ConnectionRevision, EntityKind EntityKind,
    TransferInspection Inspection);
