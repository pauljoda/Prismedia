namespace Prismedia.Contracts.Entities;

/// <summary>
/// Request body to create a user-managed taxonomy entity such as a tag, person, or studio.
/// Only the title is required; richer metadata is applied afterward through the metadata patch
/// endpoint so creation stays a single, simple step the user confirms with one save.
/// </summary>
/// <param name="Title">Display name for the new entity. Required; whitespace-only titles are rejected.</param>
/// <param name="IsNsfw">When true, marks the new entity NSFW. Defaults to false.</param>
public sealed record EntityCreateRequest(string Title, bool IsNsfw = false);
