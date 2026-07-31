namespace Prismedia.Contracts.Entities;

/// <summary>
/// Batched folder-row context for projected catalog listings (series/season rows): the
/// metadata a list row needs beyond the thumbnail projection, loadable for a whole page of ids with
/// one grouped query per collection instead of a full per-row detail hydration.
/// </summary>
/// <param name="ChildCount">Direct visible (non-wanted) child count — a series' seasons, a season's episodes.</param>
/// <param name="Description">Entity description used as the row's overview, when present.</param>
/// <param name="Dates">Named dates (premiere/air/release …) for premiere-date and production-year resolution.</param>
/// <param name="LifetimeStart">Semantic lifetime start, when the entity carries one.</param>
/// <param name="LifetimeEnd">Semantic lifetime end, when the entity carries one.</param>
/// <param name="ExternalIds">Provider identities projected as external provider ids.</param>
public sealed record EntityFolderListContext(
    int ChildCount,
    string? Description,
    IReadOnlyList<EntityDate> Dates,
    EntityDate? LifetimeStart,
    EntityDate? LifetimeEnd,
    IReadOnlyList<EntityExternalId> ExternalIds);
