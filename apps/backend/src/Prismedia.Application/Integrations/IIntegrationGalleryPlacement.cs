using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Application.Integrations;

/// <summary>Publishes an entire verified image group without exposing partial galleries or replacing existing content.</summary>
public interface IIntegrationGalleryPlacement {
    #region Abstract Methods

    /// <summary>Returns an exact already-published gallery after validating every accepted member, or null when no final folder
    /// exists.</summary>
    Task<PlacedIntegrationGallery?> ReadPlacedAsync(Guid operationId, IntegrationTransferPlan plan, LibraryRootData root,
        IntegrationGalleryOutputSet outputs, CancellationToken cancellationToken);

    /// <summary>Retries reuse only an exact existing group; conflicting or additional content requires review.</summary>
    Task<PlacedIntegrationGallery> PlaceAsync(IntegrationGalleryPlacementRequest request, CancellationToken cancellationToken);

    #endregion
}
