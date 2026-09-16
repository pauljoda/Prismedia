using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Idempotent request to import one explicitly selected full-publication offer into a writable local library.</summary>
public sealed record AcquireCatalogOfferRequest(Guid OperationId, string SelectionToken, string OfferId, Guid LibraryRootId);

/// <summary>Public durable acquisition progress without source locators, byte authorization, or staging paths.</summary>
public sealed record IntegrationTransferResponse(Guid Id, Guid ConnectionId, string Title, EntityKind EntityKind, Guid LibraryRootId,
    IntegrationTransferMode Mode, IntegrationTransferPhase Phase, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    int ArtifactCount, IReadOnlyList<Guid> ImportedEntityIds, string? LastError);
