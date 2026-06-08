# Prismedia Design & Architecture Audit

_Date: 2026-06-05 · Method: 12 specialized finder agents read the real source, every material finding was adversarially re-verified against the cited files, then synthesized. 102 verified findings (4 critical · 41 high · 39 medium · 18 low)._

> **Note:** The automated synthesizer pass was cut off by a session limit; this report is assembled from the verified per-area findings. Per-finding `file:line` granularity beyond what's quoted here lives in the workflow subagent transcripts.

---

## Executive Summary

Prismedia is, bluntly, **better-architected than most codebases its size** — the bones match the stated philosophy closely. There is one `Entity` aggregate parameterized by an `EntityKind` enum, a reflection-driven `EntityKindRegistry` that makes "add a kind = add an enum member," a genuinely elegant `EnumCodec`/`CodecRegistry` that single-sources every closed identifier, and a strong core-UI backbone (`EntityIndexPage`, `EntityGrid`, `EntityThumbnail`, `EntityDetail`) that collapses every browse route to a ~15-line wrapper.

The debt is **not in the design — it's in finishing the discipline the design already established.** Two patterns recur across all five priorities:

1. **String identifiers leak in exactly the places the codec pattern hasn't reached yet** (the Jellyfin projection, job/settings layers, capability sub-codes, and — most importantly — the OpenAPI→orval boundary, which silently downgrades every codec enum to `string`). This is your #4 priority and the single largest theme (43 findings).
2. **The "authored once" promise breaks at the seams** — capabilities are hand-written in three parallel places, detail routes hand-roll identical lifecycle code (with `movies/[id]` a near-verbatim 880-line fork of `videos/[id]`), and 52 frontend files hand-roll raw form controls while strong shared primitives sit at 2–3 importers.

There is also **one live correctness bug**: `CollectionRuleEngine` still emits SQL against the dropped `deleted_at` column.

### Grades by priority

| # | Priority | Grade | One-line |
|---|----------|:-----:|----------|
| 1 | Domain uniformity | **A−** | Exemplary core shape; debt is one layer down in capability sub-codes & the inline-vs-capability boundary. |
| 2 | Layer cleanliness | **B** | Endpoints are genuinely thin; concentrated debt (misplaced infra in Api, magic-string problem codes, telescoping `ListAsync`). |
| 3 | Duplication & helpers | **B−** | Strong shared cores, but mapper/upsert boilerplate and detail-route copy-paste are substantial and well-bounded. |
| 4 | String IDs | **C+** | Excellent foundation (`EnumCodec`, generated `codes.ts`) undermined by uneven adoption + the OpenAPI enum-as-`string` gap + jobs/contracts drift. |
| 5 | UI decomposition & unification | **B−** | Decomposition is strong (A−); primitive **unification** is the weak point (C) — three competing text inputs, 52 raw-control files. |

---

## What's Working Well

Preserve these — they're the spine everything else should be pulled back toward:

- **One entity shape.** `Prismedia.Domain/Entities/Entity.cs` is a single abstract aggregate; concrete kinds are thin sealed subclasses that override only `Kind` and `CreateDefaultCapabilities()` and compose reusable `EntityCapability` modules instead of redeclaring members.
- **Registry as single source of truth.** `EntityKindRegistry` reflects `[Code]` + `[EntityKindMeta]` off enum members — no hand-maintained descriptor table.
- **The codec pattern.** `EnumCodec<T>` + `[Code]` + `CodecRegistry` drives `EntityKind`, `JobType`, `RelationshipKind`, `BookType`, `BookFormat`, `FileSourceKind`, `EntityFileRole`, `CreditRole`, … one mechanism for DB + JSON. This is the right model for _every_ closed identifier.
- **Capability infrastructure.** `CollectionCapability<TItem>` base removes list boilerplate; `[CapabilityKind]` + `CapabilityPolymorphism` reflection with **startup validation** for missing/duplicate kinds; one `IEntityCapabilityMapper` per capability, DI-discovered, so the repository stays a coordinator.
- **Thin endpoints.** Per-entity endpoint files are 16–40 lines, delegating to a shared `MapEntityKindRoutes` helper; CRUD is generalized over the uniform `Entity`.
- **Codegen hub.** `Api/Codegen/CodesManifest.cs` + `scripts/gen-codes.mjs` emit `generated/codes.ts`, re-exported through `entities/entity-codes.ts`. Where consumed (`entity-grid.ts`, `app-settings.ts`, `entity-relationship-thumbnails.ts`) it's exemplary.
- **Core-UI backbone.** Every browse route is a ~15-line `EntityIndexPage` wrapper; constrained views (comics/ebooks) are prop-driven, not forks. `VideoPlayer` (2088 lines) already delegates to 6 child components + ~15 tested `.ts` modules — only ~200 lines are CSS.

---

## Findings by Priority

### 1. Domain Uniformity

**Strengths:** see above — the top-level shape is the best part of the codebase.

| Sev | Finding | Where / Fix |
|-----|---------|-------------|
| High | **Capability boundary is split.** Rating, Flags, Links (Urls/ExternalIds), and Files are inline fields/methods on `Entity` in the Domain, yet the **Contracts** layer models exactly these as capabilities (`RatingCapability`/`FlagsCapability`/`LinksCapability`/`FilesCapability`). The projector hand-bridges the two halves. | `Domain/Entities/Entity.cs` vs `Contracts/Entities/Capabilities/*`. Lift the inline concerns into real capabilities so the concept is "a capability" on both sides. |
| High | **Triple authoring of every capability.** Each capability is written by hand in three parallel places — Domain capability, Infrastructure persistence mapper, Contracts record + a hand-coded arm in `EntityCardProjector.MapCapabilities` — with **no parity test**. Drift-prone. | Collapse via codegen or a shared mapping contract; add a capability-parity test that fails when the three fall out of sync. |
| Medium | **Per-kind capability presence is imperative.** Each entity's `CreateDefaultCapabilities()` hardcodes which capabilities it has, instead of being registry-data-driven the way every _other_ per-kind fact already is. | Drive default-capability sets from `EntityKindRegistry` metadata. |
| Medium | **`Person` is an ad-hoc property bag** while every other metadata concern is a capability. | Migrate Person's fields onto capabilities for uniformity. |
| Medium | **A few kind-specific facts hardcoded in lists** instead of being registry-derived. | Move into `[EntityKindMeta]`. |
| Low | **Dead `Entity` constructor parameters.** The base ctor's `children`/`relationships` params are unused — every subclass re-loops `AddChild`. | Remove the dead params or actually use them. |

> The capability **sub-identifier** magic strings (`CapabilityPosition.Code`, `CapabilityStats.Code`, `CapabilitySource.Code`, `CapabilityProgress.Unit`/`Mode`, `EntityImageAsset.Kind`, `CapabilityClassification.System`, and the collection-capability codes `"season"`/`"page"`/`"track"`/`"library-root"`) are tracked under **Priority 4** — they're the same class of problem.

### 2. Layer Cleanliness

**Strengths:** thin per-entity endpoints, generalized CRUD, `CodesManifest`.

| Sev | Finding | Where / Fix |
|-----|---------|-------------|
| High | **Misplaced infrastructure inside the API project.** `Api/Endpoints/System/UpdateCheckEndpoints.cs` (476 lines) defines a whole infra service — `GhcrUpdateCheckService` doing HTTP registry auth, manifest digest, tag pagination, version parsing — **plus its port interface** — inside `Endpoints/`. | Move the service to `Prismedia.Infrastructure` behind a port; leave a thin endpoint. |
| High | **`IEntityReadService.ListAsync` is a 20+ parameter telescoping signature** taking raw strings (`kind`, `sort`, `status`, `bookType`, `bookFormat`, `relationshipCode`) straight from the query string. | Introduce a typed query/request object; parse strings into enums at the boundary. |
| High | **Problem codes are magic strings, not single-sourced.** 92 hand-written `ApiProblem` instances with bare codes (`entity_not_found` ×13, `playback_item_not_found` ×9, …); no `ProblemCodes` constants class; absent from `CodesManifest`; `entity_not_found` is hand-duplicated on the frontend. | Add a `ProblemCodes` constants class, route all `ApiProblem` through it, publish via `CodesManifest`. _(Also Priority 4.)_ |
| Medium | **Command-result→`IResult` translation is reimplemented per endpoint** (`Collections`, `EntityKindRoutes`, `EntityImageAsset` each hand-roll a status switch) while `Files` already has a clean shared `ResultOrError`/`ToProblem` helper that the rest ignore. | Adopt the `Files` helper everywhere. |

> _Coverage note:_ the Application-layer finder returned a thin summary, so fat-service analysis is lighter here. The substantive app-layer issues that did surface (e.g. `JellyfinCatalogService` dispatching on string literals) are captured under Priority 4.

### 3. Duplication & Helpers

**Strengths:** `EfEntityReadService`/`EfEntityRepository` are true coordinators that never branch on a concrete kind; `EntityIndexPage` collapses all index routes; `entity-detail-state.ts` and `entity-relationship-thumbnails.ts` centralize shared logic; `format.ts` owns the formatting helpers.

**Backend:**

| Sev | Finding | Where / Fix |
|-----|---------|-------------|
| Critical | **Live bug: `CollectionRuleEngine` emits SQL against the dropped `deleted_at` column** — a runtime failure the SQL-string tests can't catch. (Contradicts the hard-delete-only model.) | `Infrastructure/Collections/CollectionRuleEngine.cs`. Remove the `deleted_at` predicate; add a test that executes against the real schema. |
| High | **14 capability mappers + 10 kind mappers repeat the same Clear/Hydrate/Persist and `ProjectDetail`/`Track` templates** dozens of times. | Extract a generic capability-mapper base class + a shared `ProjectDetail` base-property copier. |
| High | **12 `LibraryScanPersistenceService.UpsertX` methods are near-verbatim copies** — the source-`EntityFileRow` block alone appears 13×. | Extract a single `CreateSourceRootedEntity` helper. |
| Medium | **Thumbnail tag query hand-codes `"tags"`** instead of `RelationshipKind.Tags.ToCode()`. | One-line fix. _(Also Priority 4.)_ |

**Frontend:**

| Sev | Finding | Where / Fix |
|-----|---------|-------------|
| High | **Detail routes (14–18 of them) hand-roll identical lifecycle code** — load/error/nsfw-reload/breadcrumb scaffolding, the rating/favorite/organized/metadata handler quartet, and copy-pasted error-notice markup+CSS. No `EntityDetailPage` shell. | Extract an `EntityDetailPage` wrapper + a shared detail-page lifecycle hook. |
| High | **`movies/[id]` is a near-verbatim ~880-line fork of `videos/[id]`** — it even reaches across the route boundary to import the other route's helpers. The same 19 playback/transcript/rating handlers and a 35-prop `VideoPlayer` block are duplicated. | Collapse movies into videos; extract a shared video-playback page hook/component. |
| Medium | **`people`/`studios`/`tags` pages are near-clones** of each other. | Config-driven taxonomy detail page. |
| Medium | **`lib/api/prismedia.ts` (1105 lines) is now a zero-importer duplicate** of the domain barrels. | Delete it. |
| Medium | **Stray formatters reimplemented** (bytes, seconds-to-timestamp, resolution) instead of living in `format.ts`. | Consolidate into `format.ts`. |

### 4. String IDs — _the headline theme (43 findings)_

**Strengths:** the codec foundation is genuinely strong; `generated/codes.ts` is auto-generated from `[Code]`-attributed enums; `JellyfinProtocol` centralizes the wire vocabulary; `entity-grid.ts`/`app-settings.ts` are model citizens.

The problem is **uneven adoption + one structural gap**:

| Sev | Finding | Where / Fix |
|-----|---------|-------------|
| Critical | **OpenAPI downgrades codec enums to `string`.** Because codec enums serialize via a custom `JsonConverterFactory`, the OpenAPI schema describes every enum DTO field as bare `string` (`JobRun.type`/`status`, `EntityKindCount.kind`, `EntitySubtitle.source`/`format`, `targetKind`, …). The orval-generated models therefore **lose all enum typing**, and consumers fall back to magic strings. This is the root cause feeding most frontend string drift. | Emit OpenAPI `enum` schemas for codec types (schema filter/transformer) so orval generates typed unions. |
| High | **`JellyfinCatalogService.*` ignores `EntityKind` entirely** and dispatches on hand-typed lowercase literals (`"video"`, `"audio-library"`, `"music-artist"`, `"video-series"`…), and retypes Jellyfin scalar protocol values (`"FileSystem"`, `"Full"`, `"VideoFile"`) that belong in `JellyfinProtocol`. | Consume the `EntityKind` registry + `JellyfinProtocol`; the canonical sources already exist. |
| High | **Job handlers + settings registry hand-type `EntityKind` codes** (`"video"`, `"audio-track"`, `"image"`, `"book"`). | Use `EntityKindRegistry`/codes. |
| High | **Cross-boundary position vocabulary has no shared source** — `"season"`/`"episode"`/`"track"`/`"sort"` is written in Infrastructure and re-read in Application as bare strings. | Promote to a codec enum. |
| High | **The jobs surface is the worst offender.** `lib/jobs/models.ts` + `lib/jobs/jobs-dashboard.ts` hand-maintain a fictional BullMQ queue/status vocabulary and a full `_JOB_DEFINITIONS` table re-typing every backend `JobType` as string literals, plus `mapJobStatus` that translates real `JobRunStatus` codes into invented ones — bypassing the published `JOB_TYPE` const entirely. | Consume `JOB_TYPE`/`JobRunStatus` from `codes.ts`; delete the invented vocabulary. |
| High | **`packages/contracts` carries stale, drifted declarations** that duplicate `codes.ts`: `jobs.ts` BullMQ `queueDefinitions`; `frontend-dtos.ts` `EntityKind` with **`"performer"` instead of `"person"`** and missing half the kinds; `external-ids` with extra keys. | Delete the duplicates in favor of `codes.ts`, or regenerate them. |
| High | **List sort/filter keys are stringly-typed in both directions** — backend private `ListSort` parsed from string aliases vs frontend `EntityGridSort` union — with no shared source. | Single-source via a codec enum + generated const. |
| High | **Generated consts exist but are unused.** `ENTITY_KIND` has 23 importers yet **77 raw kind literals remain** in production; `JOB_TYPE`, `PLAYBACK_MODE`, `SUBTITLE_*`, `CREDIT_ROLE`, `EXTERNAL_ID_PROVIDER`, `IDENTIFY_QUEUE_STATE`, `IDENTIFY_RESULT_STATUS`, `FILE_SOURCE_KIND` have **zero** frontend usage despite their literals being used heavily. | Adopt the consts at their literal sites. |
| High | **No automated guard that `codes.ts` is in sync.** It only regenerates manually against a running dev API, so it can silently go stale. | Add a CI parity check. |
| Medium | **Capability sub-identifiers are magic strings** (`CapabilityPosition.Code`, `CapabilityStats.Code`, `CapabilitySource.Code`, `CapabilityProgress.Unit`/`Mode`, `EntityImageAsset.Kind`, `CapabilityClassification.System`, and collection codes `"season"`/`"page"`/`"track"`/`"library-root"`) — hand-duplicated across Domain/Application/Infrastructure. | Code-key them with the existing `EnumCodec` pattern. |
| Medium | **Capability kinds declared twice on the frontend** (`codes.ts` `CAPABILITY_KIND` + 18 generated `entityCapability*CapabilityKind.ts` consts). | Pick one source. |
| Medium | **`BookType`/`BookFormat` hand-typed in the grid** (and stale/wrong in `frontend-dtos.ts`) though the backend enums carry `[Code]`. | Generate them like the other codes. |
| Medium | **`queueDefinitions` + the entire `COLLECTION_RULE_FIELDS` DSL are verbatim-copied** between `packages/contracts` and the web app. | Single-source. |
| Medium | **Core components type `kind`/`entityKind` as plain `string`**, so raw `kind="movie"` in every route is unchecked and can drift from `ENTITY_KIND`. | Type them as the generated union. |

### 5. UI Decomposition & Unification

**Decomposition is strong** (`VideoPlayer` already delegates to 6 children + 15 tested `.ts` modules; readers share `ReaderShell`; routes extract `video-page-state.ts`). The remaining offenders are bounded:

| Sev | Finding | Where / Fix |
|-----|---------|-------------|
| Medium-High | **`EntityDetail.svelte` is 2723 lines** — but **1251 are a single CSS block** and ~35 near-mechanical inline section snippets dispatched by a giant string `if/else`. | Extract the CSS and turn the snippet dispatch into section components. |
| Medium | **`EntityThumbnail.svelte` carries ~240 lines of pointer/touch scrub interaction state inline** that belongs in a testable `.svelte.ts`. | Extract to a state module. |
| Medium | **`EntityGridToolbar.svelte` is a 32-prop monolith** with 882 CSS lines. | Split props/sections. |

**Unification is the real gap** — strong primitives exist but the codebase routes around them:

| Sev | Finding | Where / Fix |
|-----|---------|-------------|
| High | **Primitives are barely adopted.** Canonical `TextInput` is used in **3 files**, `forms/TextField` in **2**, while **52 files** hand-roll raw `<input>`/`<select>`/`<textarea>`. | Migrate the 52 files; make the primitive the only path. |
| High | **At least three competing text-input style definitions** that visually diverge: ui-svelte `TextInput` (`bg-surface-1`/`border-border-default`/fixed-height) vs `forms/TextField` (`bg-surface-2`/`border-border-subtle`/`py-2`) vs `app.css .control-input`. The **date input alone is implemented five different ways**. | Collapse to one canonical text input + one date field. |
| Medium | **Search boxes (Search icon + raw input) hand-rolled in ~16 files** with no shared `SearchInput`. | Add a `SearchInput` primitive. |
| Medium | **Chips/badges splintered** across the `Badge` primitive, `forms/ToggleChip`, `app.css .tag-chip*` (10 files), `.chip-input-container`/`.chip-removable`, and inline chip buttons. | Route through `Badge`/one chip primitive. |
| Medium | **Two near-identical ~340-line popover selects** (`ProviderSelector`, `IdentifyProviderSelect`) share most trigger/menu styling. | Extract a shared `Combobox`/popover-select. |
| Medium | **Three independent card implementations** — `EntityThumbnail`, the unused `ui-svelte/MediaCard`, and a hand-built `SearchResultCard`. | Consolidate onto `EntityThumbnail`; delete or repurpose `MediaCard`. |

> _Legitimately bespoke (leave alone):_ range sliders (`QualitySlider`, `SettingsControl`, `EntityGridToolbar`), file/contenteditable controls in the readers.

---

## Coverage Gaps & Additional Notes

- **Application-layer depth.** The app-layer finder returned a terse summary; a focused follow-up pass on the large Application services (`JellyfinCatalogService` + partials, `AppSettingsRegistry`, `SettingsService`, `PlaybackInfoService`) would sharpen Priority 2 beyond what's captured here.
- **Worker** (`Prismedia.Worker`) was not deeply audited.
- **Parity tests are the connective tissue missing throughout** — the capability triple-authoring, `codes.ts` sync, and the SQL-against-real-schema bug all share the same root cause: no test that fails when hand-maintained mirrors drift. Several recommendations converge on "add the guard."

---

## Recommended Sequencing

### Tier 1 — Quick wins (small effort, high value)

1. **Fix the `CollectionRuleEngine` `deleted_at` SQL bug** — it's a live runtime failure. _(Critical, small.)_
2. **Replace the `"tags"` magic string** with `RelationshipKind.Tags.ToCode()`.
3. **Delete the dead `prismedia.ts`** (zero importers) and the stale `packages/contracts` duplicates (`"performer"`→`"person"`, BullMQ `queueDefinitions`).
4. **Swap `JellyfinCatalogService` + job-handler + settings string literals** for the existing `EntityKind` registry/codes — mechanical, the canonical source already exists.
5. **Add a `ProblemCodes` constants class**, route the 92 `ApiProblem` codes through it, publish via `CodesManifest`.
6. **Adopt the already-generated consts** (`JOB_TYPE`, `PLAYBACK_MODE`, `SUBTITLE_*`, `CREDIT_ROLE`, `EXTERNAL_ID_PROVIDER`, `IDENTIFY_*`) at their literal sites.
7. **Consolidate stray frontend formatters** into `format.ts`.

### Tier 2 — Structural refactors (medium/large)

1. **Close the OpenAPI codec-enum→`string` gap** so orval emits typed enums, then **add a CI parity guard for `codes.ts`.** This is the highest-leverage item for Priority 4 — it makes whole classes of frontend string drift _unrepresentable_.
2. **Extract an `EntityDetailPage` shell + shared detail-page lifecycle hook**; collapse `movies/[id]` into `videos/[id]`; build a config-driven taxonomy detail page. Biggest frontend duplication win.
3. **Extract a shared video-playback page hook/component** (the 19 duplicated handlers + 35-prop `VideoPlayer` block).
4. **Backend mapper/upsert dedup**: generic capability-mapper base + `ProjectDetail` base-property copier + `CreateSourceRootedEntity` scan helper.
5. **Finish the capability pattern**: lift `Rating`/`Flags`/`Links`/`Files` into real capabilities, collapse the triple authoring (codegen or shared mapping), add a capability-parity test; **code-key the capability sub-identifiers** via `EnumCodec`.
6. **Typed query object for `ListAsync`** + adopt the shared command-result→`IResult` helper everywhere; move `GhcrUpdateCheckService` out of `Api` into `Infrastructure`.
7. **Primitive unification**: pick one canonical `TextInput`/`Select`/date field, add `SearchInput` + one chip primitive + a shared `Combobox`, then migrate the 52 raw-control files; type core-component `kind` props as the generated union.

### Tier 3 — Watch / nice-to-have

- Make per-kind capability presence registry-data-driven; migrate `Person` to capabilities.
- Remove the dead `Entity` ctor `children`/`relationships` params.
- Decompose `EntityDetail` (extract CSS + section components), `EntityGridToolbar`, and `EntityThumbnail` scrub state.
- Generate `BookType`/`BookFormat`; single-source the `COLLECTION_RULE_FIELDS` DSL; pick one source for capability-kind consts.
