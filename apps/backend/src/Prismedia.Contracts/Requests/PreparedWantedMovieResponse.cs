namespace Prismedia.Contracts.Requests;

/// <summary>A reviewed movie saved without acquisition or monitoring; an existing source remains independently owned.</summary>
public sealed record PreparedWantedMovieResponse(Guid EntityId, string Title, bool HasFile);
