using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Rehydratable progress for one wanted work, its connection, and its immutable library boundary.</summary>
public sealed record ManagedRequestState(Guid OperationId, Guid ConnectionId, Guid EntityId, Guid LibraryRootId,
    long Revision, ManagedRequestPhase Phase, string? RemoteId = null, bool ReviewRequired = false);
