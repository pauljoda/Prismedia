# Audit Addendum — 2026-06-08

Reconciles the [2026-06-05 design audit](./design-audit-2026-06-05.md), the
[magic-string plan](./magic-string-elimination-plan.md), and the
[UI building-block catalog](./ui-building-block-catalog.md) against a 54-commit
bug-fix sprint (PRs #5–#25 / APP-157→178). The base inventories
(`sweep-*.json`) remain a **2026-06-05 snapshot**; this addendum records the deltas.

**Sprint shape:** 81 files, +6,265 / −373. Almost entirely bug fixes (lightbox/feed
playback, audio scan, identify UI, Jellyfin compat) plus two small features
(Jellyfin "unwatched libraries", SFW profile-visibility toggle). **No** structural
refactors and **no** work against the two initiatives — so the plans stand essentially
unchanged, with the adjustments below.

---

## Resolved

- **🐛 (was the audit's only Critical) — `CollectionRuleEngine` `deleted_at` bug is now fully fixed.**
  Their `a2050776 fix: prevent collection rule creation errors` fixed the **first** of
  the two sites (the main `WHERE`). The **second** site — the `imageCount` child-count
  subquery (`TranslateChildCount`, was line 339) — still referenced the dropped column,
  so an `imageCount` rule on a gallery/book would still have failed. **Completed this
  turn** + guarded by `CollectionRuleEngineSqlTests.GeneratedSqlNeverReferencesDroppedDeletedAtColumn`
  (exercises the gallery `imageCount` path). All 3 rule-engine SQL tests pass.

## Progress (toward the plan, by the user)

- **Priority 4 — first canonical-code adoption in `CollectionRuleEngine`.** It now uses
  `EntityKindRegistry.ToCode(kind)` for its `TargetKinds` loop and
  `RelationshipKind.Tags.ToCode()` / `RelationshipKind.Cast.ToCode()` for relations —
  exactly the pattern the magic-string plan prescribes. (Residual literals there:
  resolution buckets `"4K"/"1080p"/…`, DSL field names `"title"/"rating"/…`, and the
  `"tag"`/`"person"` relation entity-kinds — still in scope under plan §D / entity-kind.)
- **Priority 5 — good decomposition precedent.** New `identify-route-actions.ts`
  (+ test) extracts route action logic into a tested `.ts` module — the exact
  "move logic out of `.svelte` into testable `.ts`" direction the UI catalog calls for.
  Use it as the reference pattern when splitting `EntityDetail`/route pages.
- **Codegen pipeline confirmed healthy.** The new `allow_sfw` field flowed through to
  regenerated `jellyfinProfile*.ts` models, and `allow_sfw` is a proper EF column (not a
  magic-string setting key). The `[Code]`→`codes.ts` machinery the plan builds on is live.

## Regression / accretion (the headline)

- **The magic-string surface GREW.** The sprint added **~40 new entity-kind string
  literals** and **~12 new Jellyfin protocol literals** (`"Primary"`, `"SortName"`,
  `"Video"`, `"Audio"`, `"Recursive"`, `"ParentId"`, …), concentrated in the
  `JellyfinCatalogService.*` partials — **the same file the audit named the worst
  string-ID offender.** New occurrences include `"video-series"`, `"video-season"`,
  `"music-artist"`, `"audio-library"`, `"movie"`, `"collection"`, etc.
- **`JellyfinCatalogService.cs` grew 806 → 1,135 lines** (+329) adding the unwatched-
  libraries + SFW-visibility logic — so the "fat Application service" finding (Priority 2)
  also got worse.
- **Takeaway:** unguarded, identifiers accrete with every feature. This *raises* the
  priority of magic-string **Phase 0 (rails + guardrails)** — they must land before, or
  alongside, the bulk cleanup, or the cleanup loses ground to new code. The new
  `JellyfinContentVisibility.cs` and the unwatched-libraries / SFW paths should be added
  to the Jellyfin-layer sweep scope.

## Unchanged (still valid as written)

- All large-component findings: `EntityDetail.svelte` **2,738** (was 2,723),
  `VideoPlayer.svelte` 2,094, `EntityGridToolbar.svelte` 1,485, `EntityThumbnail.svelte`
  1,440 — decomposition targets stand.
- The 52 raw-control files / primitive-unification gap, the three competing text inputs,
  the movies↔videos detail fork, the EntityDetailPage extraction — all unchanged.
- The **OpenAPI codec-enum→`string` gap** (root cause of frontend code drift) is
  untouched and remains the highest-leverage structural guardrail.
- Backend infra duplication (capability/kind mappers, scan upserts) — unchanged.

## Net effect on the two plans

Both plans are **valid as-is**; no re-sequencing needed. One emphasis change: because
new violations are actively being introduced, **start with magic-string Phase 0** (author
the missing `[Code]` enums + constant classes, fix the OpenAPI enum emission, and stand up
the analyzer / `codes.ts` CI parity / `no-magic-codes` lint in warn-then-block mode) so the
discipline is enforced before the mechanical sweep. The `AGENTS.md` "Identifier Discipline"
and "UI Composition Discipline" contracts are already in place to guide it.
