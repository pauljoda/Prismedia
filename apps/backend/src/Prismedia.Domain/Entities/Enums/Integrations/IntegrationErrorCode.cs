namespace Prismedia.Domain.Entities;

/// <summary>A provider-confirmed integration failure that callers may handle without inspecting display text.</summary>
public enum IntegrationErrorCode {
    /// <summary>The connected manager definitively no longer contains the requested holding.</summary>
    [Code("managed-item-not-found")] ManagedItemNotFound
}
