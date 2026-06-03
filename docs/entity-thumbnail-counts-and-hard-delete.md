# Spec: Hard Deletes + Entity Reference Counts

Two pieces of work, executed back to back. Part 1 removes vestigial soft-delete so the data
model is clean; Part 2 adds a self-maintaining reference-count capability to the shared
thumbnail projection, building on the cleaner delete semantics from Part 1.

---

## Part 1 — Replace soft delete with hard delete

### Problem

Soft delete (`EntityRow.DeletedAt`) is half-implemented and inconsistent:

- Only two paths ever set it: taxonomy delete (`EntityManagementService.DeleteAsync`) and
  collection delete (`CollectionCommandService.DeleteAsync`).
- Scan cleanup already hard-deletes stale media (`LibraryScanPersistenceService` →
  `EntityRow.RemoveRange`).
- No restore / trash / recycle feature exists anywhere. A soft-deleted row is gone forever,
  just invisibly.
- No dedupe or watch-history-across-rescan logic reads tombstones.
- **It is actively buggy:** dependent rows (files, links, technical, playback, progress,
  fingerprints, detail rows) are wired `OnDelete(Cascade)`. Cascades fire only on a real
  delete, so a soft delete leaves every dependent row orphaned in the database.
- 70 manual `DeletedAt == null` filter sites across ~29 files, with **no** EF global query
  filter backing them — one forgotten `.Where` from leaking a "deleted" row.

### Decision

Remove soft delete entirely. The two soft-delete paths become hard deletes (`Remove`), the
`deleted_at` column is dropped, and all `DeletedAt == null` filters are removed.

### Behavior guarantees

- Deleting a taxonomy entity (tag/person/studio) or collection hard-deletes the row. Inbound
  and outbound relationship links cascade away (`entity_relationship_links` cascades on both
  `entity_id` and `target_entity_id`), so referenced media is **unlinked, not deleted**.
- `entities.parent_entity_id` FK is `OnDelete(SetNull)` — deleting a parent nulls children's
  parent rather than deleting them. Unchanged.
- The existing manual link `RemoveRange` in `EntityManagementService.DeleteAsync` becomes
  redundant under cascade; remove it and rely on the FK cascade for a single clean delete.

### Changes

1. **`EntityManagementService.DeleteAsync`** — replace `entity.DeletedAt = now` with
   `db.Entities.Remove(entity)`; drop the manual link `RemoveRange` (cascade handles it); drop
   the `row.DeletedAt == null` predicate on the load.
2. **`CollectionCommandService.DeleteAsync`** — same: `Remove(entity)`, drop the
   `DeletedAt == null` predicate.
3. **`EntityRow`** — remove the `DeletedAt` property.
4. **`BaseEntityModelConfiguration`** — remove the `deleted_at` property mapping.
5. **All read paths** — delete every `&& ... DeletedAt == null` / `.Where(x => x.DeletedAt == null)`
   filter across the ~29 files. (Audio/Video source services, plugins/identify, collections,
   organize, files, media persistence, entity read/repo, capability mappers.)
6. **EF migration** — generated from `apps/backend`, drops the `deleted_at` column and its
   default. Review before commit.

### Verification

- Full-stack restart (schema change — not HMR).
- Delete a tag that is applied to a video: the tag row is gone, the video remains, the video no
  longer lists the tag, and `entity_relationship_links` has no rows referencing the tag.
- Delete a collection: collection row, its detail row, and item rows are gone; member media
  remains.
- Grep confirms zero `DeletedAt` references remain outside migration history.

---

## Part 2 — Entity reference counts via a thumbnail contributor pipeline

### Problem

Taxonomy and collection cards (People / Studios / Tags / Collections grids) have no count
chips. We want "10 videos", "3 galleries" style chips and the same numbers available for
Jellyfin compatibility (`ChildCount` / `RecursiveItemCount`) — without over-hydrating media
grids, and without per-kind glue that has to be re-wired as the app grows.

### Decisions (confirmed)

- **Live batched aggregation**, not cached. One grouped query per grid page, same shape as the
  existing genre/tag lookup in `ProjectThumbnailsAsync`. Always correct, no invalidation, no
  scan dependency. A materialized `entity_reference_counts` projection table is a *future*
  optimization only if profiling demands it — not built now.
- **Contributor pipeline** for the thumbnail extension surface. Generalizes the inline
  accretion already happening (ParentKind, Progress, PlayCount, Genres). Each contributor runs
  always, batch-scoped to the kinds it cares about.

### Counting rule (universal, no per-code branching)

For a taxonomy/collection entity, its reference count by kind = the source entities that link
to it, grouped by the source entity's kind:

```sql
SELECT l.target_entity_id, e.kind_code, COUNT(DISTINCT l.entity_id)
FROM entity_relationship_links l
JOIN entities e ON e.id = l.entity_id
WHERE l.target_entity_id = ANY(@ids)
GROUP BY l.target_entity_id, e.kind_code
```

- `COUNT(DISTINCT l.entity_id)` — a source linked under two relationship codes (e.g. cast +
  director) counts once per kind, not twice.
- After Part 1, no `deleted_at` filter is needed: links only ever point at live entities
  (cascade removes them when either endpoint dies). The join to `entities` exists solely to
  resolve the source kind.
- The rule is kind-agnostic: any new taxonomy kind or relationship code flows through
  unchanged.

### Contract additions (`Prismedia.Contracts/Entities/EntityThumbnails.cs`)

```csharp
/// <summary>Number of source entities of one kind that reference a taxonomy/collection entity.</summary>
public sealed record EntityKindCount(string Kind, int Count);
```

On `EntityThumbnail`, one new init-only optional, null for kinds with no inbound concept
(media), populated for taxonomy/collection:

```csharp
public IReadOnlyList<EntityKindCount>? ReferenceCounts { get; init; }
```

### Contributor abstraction (`Prismedia.Application/Entities/`)

```csharp
public interface IThumbnailContributor {
    Task ContributeAsync(ThumbnailContributionContext context, CancellationToken ct);
}
```

- `ThumbnailContributionContext` carries the page's `EntityRow`s and a mutable per-id builder
  bag (extra meta chips + reference counts) the contributor writes onto.
- `EfEntityReadService.ProjectThumbnailsAsync` builds the core projection, runs each registered
  `IThumbnailContributor` once over the whole page, then materializes the final
  `EntityThumbnail`s.
- Contributors are registered via DI (`IEnumerable<IThumbnailContributor>`). Adding extra data
  later = new contributor, no call-site wiring. Each does exactly one batch query.

### Initial contributors

1. **`MediaTechnicalChipContributor`** — relocates the existing technical-chip logic out of the
   hardcoded `ProjectThumbnailMeta` switch into a contributor over media kinds. Behavior-
   preserving extraction; proves the seam.
2. **`ReferenceCountContributor`** — acts on taxonomy + collection kinds. Runs the count query
   above, writes `ReferenceCounts`, and derives display chips into `Meta`
   (e.g. `EntityThumbnailMeta("video", "10")`) so grid chips and Jellyfin compat share one
   computation. Empty references → no chip (no "0 videos").

### Chip pipeline

`Meta` is assembled from whatever contributors append, capped at `MaxThumbnailMeta`, preserving
the disc/section-first ordering guarantee. `ProjectThumbnailMeta` stops being the single source
of `Meta`.

### Index

Add an index on `entity_relationship_links (target_entity_id, entity_id)` (covering the
distinct-count group-by) via the relationship model configuration → generated migration.

### Downstream

- Regenerate the frontend client (`pnpm api:generate`, needs the running dev API).
- Consume `referenceCounts` / chips in the People / Studios / Tags / Collections grids.
- Wire Jellyfin compat counts off the same `ReferenceCounts` field.

### Verification

- Full-stack restart (schema + contract change).
- A tag applied to N videos and M galleries shows "N videos" + "M galleries" chips on its card
  and the matching numeric `ReferenceCounts`.
- A media grid page issues no reference-count query (contributor self-filters to taxonomy).
- Deleting a referencing video drops the count by one on next load (no stale cache).

### Implementation notes / deviations

- **Technical chips were not extracted into a contributor.** The base projection already loads
  `EntityTechnical` for progress, so building technical chips there is free; a separate
  `MediaTechnicalChipContributor` would re-query the same table on every media grid page. The base
  seeds `Meta`, contributors append, and the combined list is capped. The extraction can happen
  later when progress also moves to a contributor. The seam is still proven by the reference-count
  contributor (always runs, batch-scoped, DI-discovered).
- **Collections were dropped from scope.** Collection membership lives in the collection item table
  (and smart collections are rule-based), not in `entity_relationship_links`, so the inbound-link
  query returns nothing for them. Reference counts cover taxonomy (person/studio/tag). A collection
  item-count contributor over the item table is a clean follow-up.
- **Count chips merge by icon.** Kinds that share a glyph (movie + video-series + video all map to
  the "video" icon) are summed into one chip so a card shows "🎬 5" rather than three "🎬 1" chips.
  `ReferenceCounts` stays granular per kind for compatibility layers.
- **Index outcome.** The composite `(target_entity_id, entity_id)` index supersedes the
  auto-created single-column FK index, so the migration drops the latter — a net wash in index
  count.
