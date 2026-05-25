namespace Prismedia.Domain.Entities;

/// <summary>
/// A provider-specific identity for an entity, such as TMDB, AniList, Stash, or a future provider.
/// </summary>
/// <param name="Provider">Stable provider code that owns the identifier.</param>
/// <param name="Value">Provider-specific identifier value.</param>
/// <param name="Url">Optional canonical provider URL for opening the entity externally.</param>
public sealed record EntityExternalId(string Provider, string Value, string? Url);
