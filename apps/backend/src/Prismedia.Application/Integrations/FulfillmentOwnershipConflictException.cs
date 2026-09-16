namespace Prismedia.Application.Integrations;

/// <summary>A requested scope overlaps an existing acquisition owner and requires an explicit handoff.</summary>
public sealed class FulfillmentOwnershipConflictException(Exception? innerException = null)
    : ArgumentException("This work and scope already has an acquisition owner. Use its connection or resolve the existing ownership first.", innerException);
