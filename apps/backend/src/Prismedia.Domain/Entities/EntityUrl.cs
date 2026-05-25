namespace Prismedia.Domain.Entities;

/// <summary>
/// A user-visible URL associated with an entity.
/// </summary>
/// <param name="Value">Absolute external URL.</param>
/// <param name="Label">Optional label for display, such as a provider or site name.</param>
public sealed record EntityUrl(string Value, string? Label);
