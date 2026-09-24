using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Rehydratable intent progress; remote effects are fenced by a saved uncertain phase.</summary>
public sealed record ManagedControlOperationState(Guid OperationId, Guid ConnectionId, Guid HoldingId, long Revision,
    bool HasConfiguration, bool SearchRequested, ManagedControlPhase Phase, bool ConfigurationConfirmed = false,
    ManagedCommandIdentity? Command = null, ManagedCommandStatus? CommandStatus = null, bool ReviewRequired = false);
