# Code Quality Audit — 2026-07-07

Full-repo audit against the repo contract: identifier discipline (magic strings), Clean
Architecture boundaries, UI composition discipline, modularity, and dead code. Five
parallel review passes covered the .NET backend, the Svelte frontend, and packages.

**Already fixed in this pass** (commits `7afbcb06`, `5a23f8cc`): all grep-verified dead
code — the deprecated `api/prismedia.ts`/`identify.ts` facades, `image-lightbox-media.ts`,
10 orphaned lib modules, 25 unused components, dead module+test pairs, ui-svelte
`MediaCard`/`tree.ts`, and unused dependencies (`@prismedia/media-core` in web-svelte,
`postgres`, `@testing-library/user-event`). Net −5,600 lines.

**Open findings are listed below, highest leverage first.**

---

## Scorecard

| Area | Verdict |
| --- | --- |
| Backend layering (Domain/Application/Contracts/Api/Worker) | **Clean.** Zero P0s. Domain is pure and rich; endpoints return DTOs only; DbContext scoped; Worker is a thin host. |
| Backend identifier discipline | **Good homes, drifting edges.** Problem codes, capability keys, job types clean. 4 P0 literal clusters (~66 sites). |
| Frontend identifier discipline | **Weakest area.** Codegen families exist but several have zero adopters (`JOB_TYPE`, `PROBLEM_CODE`, `SUBTITLE_SOURCE`); ~63 bare `ENTITY_KIND` literals. |
| UI composition | **Contract holds.** ui-svelte is pure; every entity route uses the scaffolds. Gap: no shared Dialog primitive (12 hand-rolled dialogs). |
| Doc comments | Domain 100%, Contracts 100%, Application 97%, Infra ~74% (understated by partials), **Api 31%**. |
| Dead code | Swept and removed. One open product decision on `packages/plugins` / `media-core` / `stash-compat` (below). |

---

## Decision needed: the TypeScript packages — RESOLVED 2026-07-07: removed (plugins are .NET; Stash scrapers run on the built-in stash-compat engine)

`packages/media-core` has **zero import sites** anywhere in the repo (scan/parse logic
now lives in C#). `packages/plugins` is consumed by nothing; `packages/stash-compat`
is consumed only by `packages/plugins`. All three are `private: true`, but the
documentation site still documents a Node/TS plugin runtime that matches
`packages/plugins`.

Either these are remnants of the pre-.NET architecture (→ delete all three plus their
tests and docs pages), or they are an intended plugin-author SDK that just isn't wired
up yet (→ keep, and say so in the docs). This is a product call.

---

## P0 — Magic-string clusters (drift risk, multiple duplicate sites)

### Backend

1. **`StreamKind` has no `[Code]` enum** — `"Video"`/`"Audio"` produced at
   `MediaProbeService.cs:60` and compared at 14 sites across `HlsAssetService`,
   `VideoSourceService`, `ThumbnailService`, `VideoDirectPlayPolicy`,
   `PlaybackInfoService`. Fix: add a `StreamKind` `[Code]` enum, type
   `MediaStreamProbeResult.Type` to it (CodecCompletenessTests then covers it free).
2. **`MediaCodecs` missing** — the list `{"hevc","h265","av1","vp9"}` is byte-identical
   in `HlsAssetService.Encoding.cs:325` and `VideoSourceService.cs:246`; ~22 codec
   literals across 6 files; the canonical normalizer
   (`VideoDirectPlayPolicy.NormalizeCodecToken`) is private. Fix: introduce
   `MediaCodecs`, promote the normalizer to shared.
3. **Metadata apply field-selector vocab** (`"tags"`, `"studio"`, `"credits"`, …) —
   23 literal sites across `EntityMetadataApplyService*`, `IdentifyQueueService`,
   `ProposalApplySelection`, `EntityMetadataPatchValidator`. It is a frontend wire
   contract with no home. Fix: `MetadataApplyFields` constants + drift-guard test.
4. **`AssetPaths` not centralized** — `/assets/` retyped at 6 sites;
   `MaintenancePersistenceService.cs:70-85` reconstructs the cache directory layout in
   parallel to `AssetPathService` (a layout change silently breaks cleanup). Fix:
   public `AssetPaths` constants routed through `AssetPathService`.

### Frontend

5. **Rule-5 violation** — `lib/nsfw/hidden-entity.ts:4-9` matches the literal
   `"entity_not_found"` **and an English message regex**. `PROBLEM_CODE` has zero
   frontend consumers. Fix first; it is the wedge for problem-code discipline.
6. **`JOB_TYPE` bypassed everywhere** (~40 literals in `lib/jobs/jobs-dashboard.ts`,
   `run-catalog.ts`, `WatchedLibrariesSection.svelte`), and `lib/jobs/models.ts`
   duplicates `packages/contracts/src/jobs.ts` byte-for-byte (19 queue names + status
   unions). Fix: adopt `JOB_TYPE`, collapse the duplicate model, add a codegen home
   for queue names.
7. **~63 bare `ENTITY_KIND` literals across 22 files** (routes, search, collections,
   upload, identify, files) despite healthy adoption elsewhere.
8. **`PLAYBACK_MODE` re-declared as `"direct" | "hls"` unions in 6 files**;
   `SUBTITLE_SOURCE` duplicated twice (one copy divergent);
   `packages/contracts/frontend-dtos.ts:672` re-declares `EntityKind` and **diverges**
   (`performer` vs codegen `person`).

## P1 — Architecture drift (backend)

1. **`EntityMetadataApplyService` bypasses the domain aggregate** — writes
   `entity.Title`/`IsNsfw`/`IsOrganized` directly onto EF rows
   (`EntityMetadataApplyService.cs:118` etc.), skipping `Entity.Rename` invariants that
   every other write path enforces. Two write models for the same state.
2. **God-file regrowth after the 2026-05-31 splits** — `IdentifyQueueService.cs`
   (1,206 lines, one file, queue+search+cascade+apply+mapping) was never split;
   `JellyfinCatalogService.cs` regrew to 1,373 lines. Families:
   LibraryScanPersistence 2,894 · JellyfinCatalog 2,651 · HlsAsset 2,307.
3. **Boundary-test gaps let it happen** — `InfrastructureBoundaryTests` has no
   Domain-purity test, no Worker test, gates only `Api/Endpoints/**`, greps only the
   literal `PrismediaDbContext`, and has no max-file-size guard. Closing these makes
   the currently-clean layering self-enforcing.

## P1 — Composition gaps (frontend)

1. **No `Dialog`/`Modal` primitive** — 12 hand-rolled dialogs in two competing
   conventions (native `<dialog>` vs `div fixed inset-0`), including two confirm-dialog
   and two name-prompt near-duplicates.
2. **No `SearchInput` primitive** — magnifier+input+clear re-authored ~8×.
3. **`ConditionBuilder.svelte`** re-implements Select/TextInput styling inline; only
   raw `<select>` cluster in the app.
4. **`forms/` layer drifting to dead code** — `DateField`, `EditFormShell`,
   `SearchSelect` have zero imports while routes hand-roll date inputs. Adopt or delete.
5. **Plugin settings tabs** (`InstalledPluginsTab` + siblings) are the raw-control
   repeat offenders (12 raw buttons in one file).

## P2 — Cleanup backlog (abridged)

- Visibility/NSFW filtering reimplemented in 5 read services despite a registered
  `IEntityVisibilityChecker`; Jellyfin page-clamp duplicated across layers.
- N+1 `FindAsync`-in-loop patterns in `LibraryScanPersistenceService.VideoBatch` and
  `EntityMetadataApplyService` (background paths, small collections).
- Missing `// prism-vocab: external` annotations at the ffprobe, Stash-scraper, NFO,
  and frontend-Jellyfin parse boundaries.
- Api endpoint classes largely undocumented (31% doc-comment coverage).
- Closed sets lacking codegen homes: collection rule fields (~26 switch labels, both
  sides of the wire), provider artwork kinds, capability sub-item codes, subtitle
  rendering vocab, search kind vocab (`performer` vs `person`).
- `Infrastructure/Collections/CollectionCommandService.cs` declares
  `CollectionCommandPersistence` — rename the file.
- Hover-only actions in `VideoMarkerEditor` and `CommandPalette` (design-language rule).

## Suggested build order

1. `hidden-entity.ts` → `PROBLEM_CODE` (minutes, unlocks rule 5).
2. `StreamKind` enum + `MediaCodecs`/`MediaContainers` + shared codec normalizer.
3. `JOB_TYPE` adoption + jobs-model dedup; then the `ENTITY_KIND` sweep, enforced by a
   `no-magic-codes` lint keyed on `codes.ts` values.
4. Boundary-test hardening (Domain purity, whole-Api gate, max-file-size) + split
   `IdentifyQueueService`.
5. Route `EntityMetadataApplyService` through the aggregate (or shared invariants).
6. ui-svelte `Dialog` + `SearchInput`; migrate the 12 dialogs and 8 search boxes;
   adopt-or-delete the dead `forms/` blocks.
7. Decide the TypeScript packages question (above).
