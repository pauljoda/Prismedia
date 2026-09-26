namespace Prismedia.Contracts.Requests;

/// <summary>One reviewed Book work saved for a later rendition-specific fulfillment choice.</summary>
public sealed record PreparedWantedBookResponse(Guid EntityId, string Title, bool HasFile);
