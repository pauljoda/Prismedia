using Prismedia.Contracts.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Mutable per-page accumulator handed to each <see cref="IThumbnailContributor"/>. Carries the
/// rows being projected and collects the extra meta chips and reference counts a contributor wants
/// folded into the final <see cref="EntityThumbnail"/>s. A contributor reads <see cref="Rows"/>,
/// self-filters to the kinds it cares about, runs a single batched query, and writes its results
/// back here; the read service then merges everything onto the base projection in one pass.
/// </summary>
public sealed class ThumbnailContributions {
    private readonly Dictionary<Guid, List<EntityThumbnailMeta>> _extraMeta = [];
    private readonly Dictionary<Guid, IReadOnlyList<EntityKindCount>> _referenceCounts = [];

    /// <summary>Creates an accumulator over the rows being projected for the current page.</summary>
    /// <param name="rows">The entity rows the page is projecting into thumbnails.</param>
    public ThumbnailContributions(IReadOnlyList<EntityRow> rows) => Rows = rows;

    /// <summary>The entity rows being projected for the current page.</summary>
    public IReadOnlyList<EntityRow> Rows { get; }

    /// <summary>
    /// Appends a meta chip to an entity. No-op when <paramref name="label"/> is blank so contributors
    /// can pass optional values without guarding. Chips append after the base technical chips and the
    /// combined list is capped by the read service.
    /// </summary>
    /// <param name="entityId">Entity the chip belongs to.</param>
    /// <param name="icon">Icon code from the shared thumbnail vocabulary.</param>
    /// <param name="label">Short display label; ignored when null or whitespace.</param>
    public void AddMeta(Guid entityId, string icon, string? label) {
        if (string.IsNullOrWhiteSpace(label)) {
            return;
        }

        if (!_extraMeta.TryGetValue(entityId, out var list)) {
            list = [];
            _extraMeta[entityId] = list;
        }

        list.Add(new EntityThumbnailMeta(icon, label));
    }

    /// <summary>Sets the reference counts surfaced on an entity's thumbnail.</summary>
    /// <param name="entityId">Entity the counts belong to.</param>
    /// <param name="counts">Per-kind reference counts in display order.</param>
    public void SetReferenceCounts(Guid entityId, IReadOnlyList<EntityKindCount> counts) =>
        _referenceCounts[entityId] = counts;

    /// <summary>Extra meta chips contributed for an entity, or an empty list when none.</summary>
    public IReadOnlyList<EntityThumbnailMeta> ExtraMetaFor(Guid entityId) =>
        _extraMeta.TryGetValue(entityId, out var list) ? list : [];

    /// <summary>Reference counts contributed for an entity, or <c>null</c> when none.</summary>
    public IReadOnlyList<EntityKindCount>? ReferenceCountsFor(Guid entityId) =>
        _referenceCounts.GetValueOrDefault(entityId);
}
