# Prismedia Design & Architecture Audit

## Executive Summary

Prismedia is, by the standard of most codebases, unusually well-architected against its own stated priorities. The single biggest strength is the **uniform entity core**: one abstract `Entity` parameterized by an attribute-driven `EntityKindRegistry`, composing reusable `EntityCapability` modules, with a self-building `EnumCodec`/`CodecRegistry` that is the right model for every closed identifier — and, notably, a real backend **enforcement test layer** (`MapperCoverageTests`, `ApiProblemCodeDisciplineTests`, `ConstantsDriftGuardTests`, `CodecCompletenessTests`) that already guards much of the contract. The three biggest debts are all consistency/duplication rather than correctness: (1) the **frontend bypasses its own generated `codes.ts`** — whole code families (JOB_TYPE, PLAYBACK_MODE, IDENTIFY_*) have zero consumers while their literals are hand-typed across the API boundary, and `movies/[id]` is a drifted verbatim clone of `videos/[id]`; (2) **capability sub-codes** (position/source/progress) and the **entity-list sort/filter DSL** are free-form magic strings with no single source; (3) a **fully orphaned parallel TypeScript subsystem** (`packages/media-core` + `plugins` + `stash-compat`) re-implements scan/probe/scrape work the .NET worker now owns, in direct tension with the "no TypeScript worker" rule. None of these are firefighting emergencies — they are the visible debt against an otherwise strong design.

| Priority | Grade | One-line |
|---|---|---|
| 1. Domain Uniformity | A− | Excellent entity/capability/registry core; capability *sub-codes* and per-kind capability membership are the soft spots. |
| 2. Layer Cleanliness | B+ | Thin endpoints and coordinator repos; one misplaced infra service in Api, a 2400-line Jellyfin Application service, and orphaned TS packages drag it down. |
| 3. Duplication & Helpers | B− | Strong shared cores, but heavy mapper/scan boilerplate, a cloned video-detail route, and orphaned duplicate packages. |
| 4. String IDs | B | Best-in-class backend codec foundation + drift tests; frontend adoption is the gap — generated consts exist but are widely bypassed. |
| 5. UI Decomposition & Unification | B | Index/detail scaffolds and player decomposition are strong; primitives exist but are routed around (3 input styles, no SearchInput, chip sprawl). |

## What's Working Well

- **One entity shape, registry-driven.** `Prismedia.Domain/Entities/Entity.cs` is a single abstract aggregate; concrete kinds are thin sealed subclasses overriding only `Kind`/`CreateDefaultCapabilities()`. `EntityKindRegistry` reflects `[Code]`+`[EntityKindMeta]` so adding a kind is an enum member plus two attributes — no hand-maintained descriptor table.
- **Capability mechanism.** `EntityCapability` + `CollectionCapability<TItem>` base, attribute-driven `[CapabilityKind]` polymorphism with startup validation, and one `IEntityCapabilityMapper` per capability discovered by reflection (keeps `EfEntityRepository` a coordinator that never branches on concrete kind).
- **Codec foundation.** `EnumCodec<T>` + `[Code]` + `CodecRegistry` single-source EntityKind, JobType, RelationshipKind, BookType/Format, FileSourceKind, EntityFileRole, CreditRole; Infrastructure consistently uses `.ToCode()`/`EntityKindRegistry.Video.Code`.
- **Thin API for the common case.** Per-entity endpoint files are 16–40 lines delegating to `EntityKindRouteEndpoints.MapEntityKindRoutes`; `CodesManifest.cs` single-sources codes to the frontend codegen.
- **Backend enforcement tests are real.** `ApiProblemCodeDisciplineTests` (zero bare `new ApiProblem("...")`), `MapperCoverageTests` (1:1 capability/kind↔mapper), `ConstantsDriftGuardTests` (Jellyfin headers/ImageTypes/MIME pinned to owning classes), `CodecCompletenessTests`, `EndpointLayoutTests`/`InfrastructureBoundaryTests` (layering). Much "drift" is in fact guarded.
- **Frontend core UI backbone.** Every browse route is a ~15-line wrapper over `EntityIndexPage`; `EntityGrid`/`EntityThumbnail` are richly capable and reused for child collections; `entityCardToDetailCard` feeds one `EntityDetail`. `VideoPlayer` (2088 lines) already delegates to six child components + ~15 tested `lib/player/` modules.
- **Generated codes pipeline exists and is consumed where adopted.** `codes.ts` ← `gen-codes.mjs` ← dev manifest; `entity-grid.ts`, `app-settings.ts`, `entity-relationship-thumbnails.ts` are model citizens. `check-generated.mjs` (`pnpm api:check`) already regenerates-and-diffs.

## Findings by Priority

### 1. Domain Uniformity

- **Code-key the collection capabilities (position/source/dates) instead of magic strings** · `critical` · `medium effort`
  `CapabilityPosition.cs:14`, `CapabilityStats.cs:13`, `CapabilitySource.cs:13`, `EntityDate`, `EntityMetadataPositionRules.cs:17-46`, `Book.cs:46`. All key on a bare `string Code` documented only in XML comments (`season/episode/track/sort`, `folder/library-root/file`). Literals are re-typed across Domain/Infra/Application (scan writers, Jellyfin readers) with no compile check.
  **Fix:** introduce `[Code]` enums `PositionCode`/`SourceCode`/`DateCode` via the existing `CodecRegistry`; have `Set/Add` take the enum and encode only at the persistence edge. Keep `Stats` codes as strings (plugin-supplied, open set).
- **Lift inline cross-cutting concerns (Rating, Flags, Links, Files) into real capabilities** · `medium` · `large effort`
  `Entity.cs:85-165` implements these inline + a bespoke `HydrateUniversalProperties`, yet Contracts models all four as `EntityCapability` records and `EntityCardProjector` hand-bridges them (lines 62-63, 107-124) — the only special-cases in an otherwise uniform projector loop.
  **Fix:** model as scalar/`CollectionCapability` capabilities; keep thin convenience accessors on `Entity` (≈160 call sites); drop the projector special-cases.
- **Make per-kind capability presence data-driven** · `medium` · `medium effort`
  17 `CreateDefaultCapabilities()` overrides (`Video.cs:19`, `Movie.cs:33`, …) hardcode capability membership imperatively — the one per-kind fact the registry doesn't own.
  **Fix:** add `[DefaultCapabilities(typeof(...))]` / a `CapabilityTypes` field on the descriptor; instantiate from the registry. Verify parameterless ctors.
- **Domain hardcodes kind facts the registry could derive** · `medium` · `medium effort`
  `Collection.cs:10` (`ContainableKinds` HashSet) and `LibraryMaintenanceJobHandler.cs:17` (`CacheSubdir` table, also duplicated in `AssetPathService.cs` + `MaintenancePersistenceService.cs`).
  **Fix:** add `Collectible` bool + explicit `CacheSubdir` string to `EntityKindMetaAttribute`; route all three files through it.
- **Unify Credits projection with the capability envelope** · `medium` · `small effort`
  `CapabilityCredits` is the only capability not projected into `EntityCard.Capabilities`; `EntityCardProjector.CreditMetadata` emits a side-channel grafted per-kind (`EntityMappers.cs:88-128`).
  **Fix:** add a `CreditsCapability` contract record and project it in `MapCapabilities` like every sibling.
- **Base `Entity` ctor `children`/`relationships` params are dead** · `medium` · `small effort`
  `Entity.cs:25-51` accepts and loops them; zero callers pass them; `Movie`/`VideoSeries`/`VideoSeason` re-loop `AddChild` (VideoSeries has duplicate `children`+`videos`).
  **Fix:** drop the unused base params and VideoSeries' redundant `children`.
- **`CapabilityProgress.Unit`/`Mode` are unvalidated strings** · `high` · `medium effort`
  `CapabilityProgress.cs`, threaded unvalidated through `EntityCapabilityService.cs:182`, `EntityProgressEndpoint.cs:17`. `ReaderMode` is codec-backed but Mode round-trips as a bare string. *(Listed here for cohesion; see also §4.)*
  **Fix:** add `ProgressUnit` enum; validate Mode at the Application boundary via `CodecRegistry.TryDecodeAs<ReaderMode>` (mirror `CollectionCommandService.cs:252`).
- **Single-value capability boilerplate / `CapabilityClassification.System` open string** · `low` · `small effort`
  No `SingleValueCapability<T>` base (Description/Classification/Technical repeat the shape; stray `/// <inheritdoc/>` artifacts); `System` and `EntityExternalId.Provider` are free provider strings.
  **Fix:** add a small base; route well-known provider/system values through constants where closed.

### 2. Layer Cleanliness

- **Move `GhcrUpdateCheckService` + `IUpdateCheckService` out of `Api/Endpoints`** · `high` · `medium effort`
  `UpdateCheckEndpoints.cs` is 476 lines; only ~37 are endpoints. Lines 82–476 are a full HTTP-client infra service (GHCR auth, manifest digest, tag pagination, version parse, caching) — misplaced infra registered at `Program.cs:61`.
  **Fix:** port → `Application/System`, impl → `Infrastructure/System`, DI → `AddPrismediaInfrastructure`; extract changelog-path resolver. Collapses to ~40 lines.
- **Telescoping 23-param `IEntityReadService.ListAsync`** · `medium` · `medium effort`
  `IEntityReadService.cs:57` takes 23 raw params; `EntityListEndpoint.cs` forwards positionally; 11 `JellyfinCatalogService` call sites thread positionally/with literals (`sort:"added"`).
  **Fix:** `EntityListQuery` record bound via `[AsParameters]`; collapse to `ListAsync(EntityListQuery, bool hideNsfw, ct)`.
- **Jellyfin endpoint partials parse raw query strings + define DTO mappers inline** · `low` · `small–medium effort`
  `JellyfinCompatibilityEndpoints.cs:504` (`ItemQueryFrom` hand parsers), `.Users.cs:54/69` (duplicate `ToUserDto`).
  **Fix:** bind via `[AsParameters]`; move mappers to `Api/Mapping` (`JellyfinContractMapping`).
- **Magic-string `TargetEntityKind:"entity"` in `EntityRefreshEndpoint`** · `low` · `small effort`
  `EntityRefreshEndpoint.cs:24` passes a placeholder kind that is not a real `EntityKind` code (the bare-literal `TargetEntityKind` pattern is in fact prevalent across scan handlers — see §4).
  **Fix:** pass `null` (field is display-only) or resolve the real kind.

### 3. Duplication & Helpers

- **Collapse `movies/[id]` ↔ `videos/[id]` into one shared video-detail engine** · `high` · `large effort`
  `routes/movies/[id]/+page.svelte` (859) and `videos/[id]/+page.svelte` (915) share ~20 identical handlers + a 35-prop VideoPlayer block; movies imports `video-page-state` and `VideoDetailSectionContent` *across the route boundary*. **Already drifted**: movies sends `PositionTicks:0` on ended and lost the `beforeNavigate` resume-flush + `trackedVideoId` guard.
  **Fix:** `useVideoDetailPage(...)` rune in `$lib/player`, parameterized by kind-fetcher (`fetchVideo`/`fetchMovie` → child video); relocate the shared modules out of `routes/`. Adopt the videos version as source of truth.
- **12 scan `Upsert*Async` are near-verbatim copies** · `high` · `large effort`
  `LibraryScanPersistenceService.ScanUpserts.cs`: the source `EntityFileRow` block appears 13×; the find-or-update shape repeats. `Common.cs` has `EnsureEntityFileAsync`/`UpsertStructuralChildLinkAsync` the create path ignores.
  **Fix:** `UpsertCoreEntityAsync(kindCode, role, EntityUpsertSpec, ct)` + per-kind detail callback; fix the 4 methods mutating the `AsNoTracking` `existing`.
- **14 capability mappers duplicate Clear/Hydrate/Persist** · `high` · `medium effort`
  11 share a byte-identical `ClearAsync`; Hydrate is one of two fixed templates.
  **Fix:** two thin bases — `SingleRowCapabilityMapper<TRow,TCap>` and `CollectionCapabilityMapper<TRow,TCap>` — sharing one `ClearAsync`; Credits/Subtitles/Playback/Markers override.
- **Kind-mapper `ProjectDetail` base-copy (14 sites) + `Track` helper (×9)** · `high` · `medium effort`
  Every `ProjectDetail` re-copies the same 8 base props; 9 identical `private XDetailRow Track(...)` + `FindAsync ?? Track(new...)` preambles. (`EntityDetail` record doc already says concretes should "only declare extras.")
  **Fix:** a base-copier factory + `EnsureDetailRowAsync<TRow>(set, id, create)`; collapse the `ConventionEntityKindMapper` arms.
- **Collapse the triple capability mapping (Domain / Infra mapper / Contracts projector)** · `high` · `large effort`
  Each capability authored 3×; the contract-projector arm has 140 lines of one-off `if (entity.X is {} y) ...`. *(Structural arm is guarded by `MapperCoverageTests`; the projector arm by `EntityCardProjectorContractTests` — verify these assert field-level round-trip, not just presence.)*
  **Fix:** drive `MapCapabilities` by a `DomainType→converter` registry.
- **Delete `prismedia.ts` — 1105-line zero-importer duplicate facade** · `medium` · `medium effort`
  Re-defines `fetchVideo`/`fetchSettings`/`createEntityMarker`/`fileContentUrl` that the split barrels own; guardrail test pins importers at 0. No unique exports.
  **Fix:** delete `prismedia.ts` + `prismedia.test.ts`; remove its `trackedFacades` entry.
- **Shared entity-detail-page lifecycle hook** · `high` · `medium effort`
  ~18 detail routes hand-roll `LoadState`, `lastNsfwMode` reload-effect, breadcrumb effect, `redirectHiddenEntityNotFound` try/catch, and the rating/favorite/organized/metadata quartet (structurally identical).
  **Fix:** `use-entity-detail-page.svelte.ts` owning lifecycle + the four handlers (wrapping `entity-detail-state.ts`); pages supply the loader.
- **`ThumbnailService` repeats ffmpeg arg prefix + encode/filter strings** · `medium` · `medium effort`
  `ThumbnailService.cs`: `-hide_banner -loglevel error -y -threads` ×8; libx264 tail and trickplay filter duplicated.
  **Fix:** `CommonArgs(threads)`, `PreviewEncodeArgs()`, `TrickplayScaleFilter(w,h)`; have the combined method delegate.
- **Read-service engagement/recency predicate repeated** · `medium` · `medium effort`
  `EfEntityReadService.cs`: "has any engagement" written 3× (played-true, played-false, unwatched) + completed in watched + recency in `ApplyLastPlayedOrdering`.
  **Fix:** extract `Expression<Func<EntityRow,bool>>` `HasAnyEngagement`/`IsCompleted`/`IsInProgress` + `RecencySelector` (keep as Expressions for EF translation).
- **Consolidate stray FE formatters into `format.ts`** · `medium` · `small effort`
  Two divergent `formatBytes` (`FileDetailPane.svelte:100`, `TranscodeCacheSection.svelte:88`); seconds→timestamp in 5 files; two `formatResolution` variants.
  **Fix:** add `formatBytes(value,{dash})`, `formatSecondsTimestamp`, parameterized `formatResolution` to `format.ts`.
- **`GetAsync`/`GetDetailAsync` duplicate shallow-load + enrich** · `low` · `small effort`
  `EfEntityReadService.cs:431/462`. **Fix:** private `LoadShallowCardAsync(id, hideNsfw, ct)`.

### 4. String IDs

> Backend is largely guarded (`ApiProblemCodeDisciplineTests`, `ConstantsDriftGuardTests`). The unifying pattern: **outbound is enforced, inbound/frontend is not.**

- **Frontend bypasses generated code families wholesale** · `critical` · `medium effort`
  `codes.ts` exports `JOB_TYPE`, `PLAYBACK_MODE`, `IDENTIFY_QUEUE_STATE`, `IDENTIFY_RESULT_STATUS`, `SUBTITLE_*`, `CREDIT_ROLE`, `FILE_SOURCE_KIND` — **zero frontend consumers**. Literals used raw: `createJob("scan-library")` (`WatchedLibrariesSection.svelte:105`), `jobs-dashboard.ts` hand-types every type, `identify-store` compares `item.state === "proposal"` ×20, `VideoPlayer.svelte` assigns `playbackMode = "direct"/"hls"`.
  **Fix:** type `createJob(type: JobTypeCode)`, `JobDefinition.type`/`RunCatalogEntry.jobType` off `JOB_TYPE.*`; replace identify/playback unions with the generated codes; add the planned `no-magic-codes` lint.
- **Jobs dashboard re-declares JobType + an invented BullMQ status vocabulary** · `high` · `medium effort`
  `jobs-dashboard.ts:21` `_JOB_DEFINITIONS` (already stale: missing `generate-grid-thumbnail`, `refresh-entity`, etc.); `models.ts` `JobStatus` invents `waiting/active/delayed/paused`; `mapJobStatus` rewrites real `JobRunStatus` into fiction. No BullMQ exists in the .NET backend.
  **Fix:** key off `JOB_TYPE.*`/`JOB_RUN_STATUS.*`; derive any grouping from a typed switch; ideally expose queue grouping server-side on the DTO.
- **Hand-typed unions duplicate backend `[Code]` sets** · `high` · `small effort`
  `IdentifyQueueState` (`identify-types.ts:116`), `PlaybackMode` (×3: `video-player-types.ts:30`, `video-player-load.ts:2`, `library-settings.ts:11`).
  **Fix:** re-export `IdentifyQueueStateCode`/`PlaybackModeCode` via `entity-codes.ts`; alias the unions to them.
- **Core components type `kind` as `string`; routes pass raw kind literals** · `high` · `medium effort`
  `EntityIndexPage.svelte:48`, `EntityGrid.svelte:51`, `entity-index-page.svelte.ts:26`; every route `kind="video-series"` etc. `ListEntitiesParams.kind` is `string?` too.
  **Fix:** type props as `EntityKindCode`; routes pass `ENTITY_KIND.*`. (Drop the stale `entity-thumbnail.ts` part — already `EntityKind`-typed.)
- **77 raw entity-kind literals remain (27 in logic branches)** · `high` · `large effort`
  `EntityThumbnail.svelte:120`, `uploader.svelte.ts:47-86`, `audio/[id]`, `artists/[id]`, `galleries/[id]`, `entity-detail.ts:209`. **Live drift:** `SearchResultCard.svelte:32` compares `"performer"` (no such code — dead branch).
  **Fix:** sweep the ~27 logic comparisons to `ENTITY_KIND.*`; add `childGroupOfKind(detail, ENTITY_KIND.*)` helper; lab fixtures opportunistically.
- **Backend `EntityPosition` codes are bare literals on write + read** · `high` · `medium effort`
  `LibraryScanPersistenceService.VideoBatch.cs:88/125/304`, `EntityMetadataPositionRules.cs:17-46`, `JellyfinCatalogService.Mapping.cs:185-187/388`. *(Same root cause as the §1 critical; fix together via `EntityPositionCode`.)*
- **Settings registry hand-types codes that have enums — `SubtitleStyle` HAS drifted** · `high` · `small effort`
  `AppSettingsRegistry.cs`: `AutoIdentifyEntityKinds` raw kinds, `PlaybackDefaultMode` `direct/hls`, `HlsTranscoderProfile` names, `SubtitlesStyle` = `stylized/classic/outline` while `SubtitleStyle` enum = `stylized/plain` (FE agrees with registry → the enum is the stale orphan). *(Note: `AppSettingsRegistryTests` exists but evidently does not assert enum-source equality — a weak test, not a missing one.)*
  **Fix:** reconcile enum to `stylized/classic/outline`, regenerate; derive registry options from `CodecRegistry`/`Enum.GetNames`.
- **EntityKind codes hand-typed as `TargetEntityKind` in job handlers** · `high` · `small effort`
  `ScanLibraryJobHandler.cs:123+`, `ScanAudioJobHandler.cs:153`, `ScanGalleryJobHandler.cs:117`, `ScanBookJobHandler.cs:281/335`, `GeneratePreviewJobHandler.cs:33`, `ProbeAudioJobHandler.cs:43`.
  **Fix:** `EntityKindRegistry.Video.Code`; consider `EnqueueJobRequest(JobType, EntityKind, ...)` overload.
- **Entity-list sort/status keys stringly-typed both directions** · `high` · `medium effort`
  Backend private `ListSort` parsed from aliases (`EfEntityReadService.cs:185`); FE hand-writes `EntityGridSort` (`entity-grid.ts:34`) + `VALID_SORTS` copy; `status:*` literals in drawer/routes. `relationshipCode`/`status`/`bookType` cross as bare `string`.
  **Fix:** promote `ListSort` → public `[Code] EntityListSort` (server-reproducible only) + `EntityListStatusFilter`, into `ENUM_EXPORTS`; keep `kind`/`position` as a separate client-only sort type; type `relationshipCode` to `RELATIONSHIP_CODE`.
- **Filter-id colon-DSL parsed by hand across producer/consumer** · `high` · `large effort`
  `entity-grid.ts` `id.split(":")` / `startsWith("rating:min:")` vs `EntityGridFilterDrawer.svelte` building `"flags:favorite"` etc.; resolution tiers + duration buckets copied 4× (incl. `videos-list-prefs.ts`).
  **Fix:** `entity-grid-filters.ts` with `FILTER_KEY` consts + encode/decode helpers + single `RESOLUTION_TIERS`/`DURATION_BUCKETS`; both sides go through it.
- **`COLLECTION_RULE_FIELDS` DSL duplicated 3× (contracts, web, backend engine)** · `high` · `medium effort`
  `collections/models.ts` byte-identical to `contracts/collections.ts`; field names also hardcoded in `CollectionRuleEngine.cs:126-146`; `CollectionEntityType` re-lists ENTITY_KIND.
  **Fix:** web re-exports the contract; surface field/operator vocab from backend via CodesManifest; derive `CollectionEntityType` from `EntityKindCode`. Update `collection-detail.test.ts:52`.
- **Use `RELATIONSHIP_CODE` instead of raw `relationshipCode` literals** · `high` · `small effort`
  `studios/[id]:95` `"studio"`, `tags/[id]:82` `"tags"`, `people/[id]:114` `"cast"`; const exists and is used in `entity-relationship-thumbnails.ts`. **Fix:** import from `entity-codes`; fix `prismedia.test.ts:77`.
- **Book type/format codes hand-typed across 3 places** · `medium` (FE) / `high` (backend root) · `medium effort`
  `comics/+page.svelte:11`, `ebooks/+page.svelte:11`, `entity-grid.ts:502/510`, stale `frontend-dtos.ts:179` (`BookType="comic"`). Orval **already emits** `bookType.ts`/`bookFormat.ts`. **Fix:** map `BOOK_*_FILTER_DEFS` over the generated consts; delete the stale dead type.
- **Stale drifted contracts in `packages/contracts`** · `high` · `small effort`
  `jobs.ts` (BullMQ dup), `frontend-dtos.ts:672` `EntityKind` with `"performer"` + missing kinds (dead), `external-ids.ts:15` adds `mal/trakt`. **Fix:** delete `jobs.ts`, the dead EntityKind/Search block, and `external-ids.ts` (or key off generated codes).
- **Jellyfin scalar protocol values retyped inline** · `medium` · `small effort`
  `"FileSystem"/"Full"/"VideoFile"` ×11 (`JellyfinCatalogService.Mapping.cs` + `MediaStreams.cs:27`) while neighbors use `JellyfinProtocol.*`. **Fix:** add `LocationTypes.FileSystem`/`PlayAccess.Full`/`VideoTypes.VideoFile`.
- **Detail pages compare kind with raw literals** · `medium` · `small effort`
  `galleries/[id]:85/86/108`, `audio/[id]:78/154/166`, `artists/[id]:126/131/174`. **Fix:** import `ENTITY_KIND`.
- **`RelationshipCode == "tags"` literal in read service** · `medium` · `small effort`
  `EfEntityReadService.Thumbnails.cs:99` — the only bare relationship-code literal in Infra. **Fix:** `RelationshipKind.Tags.ToCode()` hoisted to a static field.
- **NSFW visibility mode `off/show` split across registry + edge + FE union** · `medium` · `small effort`
  `AppSettingsRegistry.cs:53` vs `EndpointRouteBuilderExtensions.cs:62` vs FE `NsfwMode` union; cookie name triplicated.
  **Fix:** `[Code] VisibilityMode` enum + generated const; single-source the cookie name.
- **`SearchEntityKind` parallel alias + hardcoded route hrefs** · `medium` · `medium effort`
  `search/models.ts:1` (`performer`/dropped kinds); `search-kind-config.ts:38` hrefs duplicate `resolveEntityBrowsePath`. **Fix:** derive from `EntityKindCode`; one `toSearchKind/fromSearchKind` alias; use `resolveEntityBrowsePath`.
- **Filter-facet `type` tokens bare in list-prefs** · `medium` · `small effort`
  `videos-list-prefs.ts:171`, `series-list-prefs.ts:132`, `filter-presets.ts:15` (`type: string`). **Fix:** local `FILTER_FACET` const; type the three ActiveFilter interfaces.
- **No CI guard that `codes.ts` is in sync** · `medium` · `small effort`
  `check-generated.mjs`/`pnpm api:check` exists but is **orphaned** — no workflow invokes it (needs the Development-only manifest endpoint). **Fix:** wire `api:check` into a CI job that boots the dev API, or add an opportunistic manifest-compare test; document the regen discipline in CLAUDE.md.
- **Lower-priority FE identifier nits** · `low–medium` · `small effort`
  Identify image-kind literals vs `ENTITY_FILE_ROLE` (`IdentifyReview*.svelte`, `identify-review-helpers.ts`); `EntityImageAsset(string Kind)` re-parsed in `JellyfinCatalogService.Metadata.cs:68` + missing `Screenshot` const; `PROVIDER_PRIORITY`/`CREDIT_ROLE` unused consts; plugins tab ids; thumbnail hover-kind discriminator; `hls.maxCacheSizeGb` literal (the key IS in `SETTING_KEYS` — only a local-const swap needed); FE MIME copy (`media-session.ts:77`).

### 5. UI Decomposition & Unification

- **Three competing input styles** · `high` · `medium effort`
  `ui-svelte/TextInput` (surface-1/border-default/fixed-height) vs `forms/TextField`/`TextAreaField`/`DateField` (surface-2/border-subtle/py-2) vs `app.css .control-input` (used in 9 files with ad-hoc patching). `NameInputDialog` already proves the wrapper pattern.
  **Fix:** make `forms/*` compose `TextInput`; add a `TextArea` primitive + `size` variant; delete `.control-*`.
- **No `SearchInput` primitive — hand-rolled in ~17 files** · `high` · `medium effort`
  `EntityGridToolbar` `.search-box` (~80 CSS lines), `DestinationPicker`, `ProviderSelector`, `IdentifyProviderSelect`, plugins tabs.
  **Fix:** add `SearchInput` to ui-svelte (icon + clear + glow) with a bare in-dropdown variant; preserve keyboard nav via forwarding.
- **Chip/badge sprawl (5 systems)** · `high` · `large effort`
  `Badge` (CVA, 6 variants) vs `.tag-chip-*` (same 6 tones, 11 files) vs `ToggleChip` vs inline `ConditionBuilder` chips vs `IdentifyReviewChoice` "Best". *(Already documented as Wave 3 in `ui-building-block-catalog.md`.)*
  **Fix:** Badge = single static-chip source (delete `.tag-chip-*`); generalize `ToggleChip` for selectable chips; sequence per the catalog.
- **`EntityDetail` 35-snippet string-dispatch + 5 parallel section-id lists** · `high` · `large effort`
  `EntityDetail.svelte:1019` if/else over bare section ids; `coreSections`/`sectionEditable`/`sectionHasDisplayContent`/`DEFAULT_STANDALONE...` must stay in lockstep; route files also hand-type the ids. Six near-identical MetadataCard snippets.
  **Fix:** `ENTITY_DETAIL_SECTION` const + typed registry `{label,icon,editable,hasContent,display,edit}` in `$lib/entities` (NOT codes.ts); collapse MetadataCard snippets to data-driven config; type tab `sections`.
- **No `EntityDetailPage` shell + `movies/[id]` clone** · `high` · `large/medium effort`
  *(Same surfaces as §3 video-detail/lifecycle-hook findings — sequence together.)*
- **Collapse people/studios/tags `[id]` into one `TaxonomyDetailPage`** · `high` · `medium effort`
  Three near-identical routes (load/handlers/error CSS); differ only by kind/breadcrumb/relationshipCode/heading.
  **Fix:** config-driven `TaxonomyDetailPage.svelte` (passing `RELATIONSHIP_CODE.*`, not literals).
- **Three card implementations** · `high` · `medium effort`
  `MediaCard.svelte` (397 lines, **zero consumers** — dead), hand-built `SearchResultCard` (search + CommandPalette), canonical `EntityThumbnail`.
  **Fix:** delete `MediaCard`; build a `SearchResultItem→EntityThumbnailCard` adapter (mapping `SearchEntityKind→ENTITY_KIND`); keep only the compact palette row if needed.
- **`EntityGridToolbar` 32-prop monolith (883 CSS lines)** · `medium` · `large effort`
  **Fix:** split into `SearchSortRow`/`ViewControlsRow`/`SelectionActionBar`; replace `"grid"/"list"/"feed"` literals with a const.
- **`EntityDetail` 1266-line CSS block** · `medium` · `medium effort`
  Travels with the section-extraction work (scoped styles follow markup).
- **Extract `EntityThumbnail` scrub interaction; consolidate with `trickplay-scrub.svelte.ts`** · `medium` · `medium effort`
  ~240 lines of pointer/touch state inline; duplicates the (currently-unadopted) `createTrickplayScrub`. **Fix:** `thumbnail-touch.ts` for pure geometry + unit tests.
- **Lightbox hydration duplicated (EntityIndexPage ↔ galleries/[id])** · `medium` · `medium effort`
  **Fix:** `createImageLightbox()` rune parameterized by per-kind hydrate + optional rating callback.
- **Two near-identical popover-selects** · `medium` · `medium effort`
  `ProviderSelector` (**dead**), `IdentifyProviderSelect` (~180 CSS lines). **Fix:** delete `ProviderSelector`; give `forms/SearchSelect` option/trigger snippets.
- **Migrate remaining inline child-grid sections to `EntityGridSection`** · `low` · `medium effort`
  Component exists and is adopted by galleries/series/books; 8 routes still inline `.content-section`. **Fix:** migrate + delete duplicated CSS (confirm collapse behavior).
- **Bespoke buttons / steppers / range sliders / settings panels / two reference rails** · `low–medium` · `varies`
  Route `IdentifyReviewChoice` buttons through `Button`; extract a parameterized `Stepper`; add a `Slider` primitive / `.range-brass` recipe; extract remaining settings panels; make `EntityCastAndCrewSection` compose `EntityDetailReferenceRail`. Composite editors (Markdown/PDF/Comic/KeyValue/List) stay bespoke but should adopt `TextInput` for inner fields.

## Coverage Gaps & Additional Notes

- **Orphaned parallel TypeScript subsystem (largest unflagged debt).** `packages/media-core`, `packages/plugins`, `packages/stash-compat` re-implement file discovery, probe, fingerprint, filename/season parsing, and Stash scraping (`executor.ts`, `xpath-scraper.ts`, `yaml-parser.ts`, `normalizer.ts`, `classifier.ts`) that the .NET worker now owns (`Infrastructure/StashCompat/StashScraperEngine.cs`, `StashXPathEngine.cs`, `StashScriptExecutor.cs`, etc.). `@prismedia/plugins` has **zero importers**; `stash-compat` only by `plugins`; `media-core` only by orphans + web. CLAUDE.md forbids a TypeScript worker. **Action: verify web consumes nothing, then delete the three packages and drop the deps** (`layer-cleanliness`, `large`).
- **Application layer was barely audited.** `JellyfinCatalogService.cs` is ~2400 lines across partials — the fattest Application service; never graded for layer-cleanliness beyond its string-id issues. `AppSettingsRegistry.cs` (634) and `CollectionCommandService.cs` (527) also un-graded for fat-service. Worth a focused pass.
- **Backend enforcement already covers several "no guard" claims** — do not re-bill: `ApiProblemCodes` is done backend-side (the live remnant is FE matching codes); capability/kind↔mapper drift is guarded; Jellyfin/MIME literals are pinned by `ConstantsDriftGuardTests`. The real residual is consistently the **frontend / inbound** half.
- **Scan-handler uniformity unexamined.** `ScanBookJobHandler.cs` (485) vs `ScanGalleryJobHandler.cs` (299) suggests per-kind divergence in the *handler* layer (priority-1 territory the entity-scoped finder never reached). Audio/comic/ebook sub-kind machinery + `media-core/classifier` were sampled lightly.
- **Worker is correctly thin** (`Program.cs`, 94 lines; all logic in Infrastructure handlers) — explicitly clean, no finding.

## Recommended Sequencing

### Tier 1 — Quick wins (small effort, high value)
1. **Frontend string-id sweep, single-source side first:** type `EntityIndexPage`/`EntityGrid` `kind` props and route literals to `ENTITY_KIND.*`; replace `relationshipCode` literals with `RELATIONSHIP_CODE.*`; alias `IdentifyQueueState`/`PlaybackMode` (×3) to generated codes; reconcile `SubtitleStyle` enum + regenerate. *(Findings: "Core components type kind", "Use RELATIONSHIP_CODE", "Hand-typed unions", "Settings registry hand-types codes".)*
2. **Backend literal mop-up:** job-handler `TargetEntityKind` → `EntityKindRegistry.*.Code`; `RelationshipCode == "tags"` → `.ToCode()`; Jellyfin `FileSystem/Full/VideoFile` → `JellyfinProtocol`; NSFW `VisibilityMode` enum.
3. **Delete dead code:** `prismedia.ts` + test; `MediaCard.svelte` + exports; `ProviderSelector.svelte`; stale `packages/contracts` `jobs.ts`/EntityKind-block/`external-ids.ts`.
4. **Consolidate FE formatters** into `format.ts` (`formatBytes`/`formatSecondsTimestamp`/`formatResolution`).
5. **Base `Entity` ctor cleanup** + **`EfEntityReadService.GetAsync`/`GetDetailAsync`** dedupe.
6. **Wire `pnpm api:check` into CI** (boots dev API) — close the codes.ts drift loophole.

### Tier 2 — Structural refactors
1. **`EntityPositionCode`/`SourceCode`/`ProgressUnit` enums** — fixes the §1 critical + the matching §4 backend findings in one stroke.
2. **Shared video-detail engine** (`useVideoDetailPage` + relocate shared modules out of `routes/`) — kills the movies/videos clone *and its live drift*.
3. **`use-entity-detail-page` lifecycle hook** + **`TaxonomyDetailPage`** + **`EntityDetailPage` shell** — collapse ~18 detail routes.
4. **Infra boilerplate bases:** `SingleRow`/`Collection` capability mappers; kind-mapper base-copier + `EnsureDetailRowAsync`; `UpsertCoreEntityAsync` + `EntityUpsertSpec`.
5. **`EntityDetail` section registry** (`ENTITY_DETAIL_SECTION` + typed registry) — eliminates the 5 parallel lists and the 35-snippet dispatch; CSS follows.
6. **Primitive unification:** `TextInput`-composed `forms/*`, new `SearchInput`, Badge-as-single-chip-source (`ui-building-block-catalog.md` Wave 3).
7. **Frontend job/identify code adoption** — type `createJob`/`JobDefinition`/identify states off generated codes; replace the invented BullMQ vocabulary.
8. **`entity-grid-filters.ts`** typed filter DSL + single `RESOLUTION_TIERS`/`DURATION_BUCKETS`.
9. **Move `GhcrUpdateCheckService`** to Infra/Application; **`EntityListQuery` record** for `ListAsync`.

### Tier 3 — Watch / nice-to-have
- **Delete the orphaned TS packages** (`media-core`/`plugins`/`stash-compat`) after confirming web consumes nothing — large but mostly mechanical; resolves the "no TS worker" violation.
- **Decompose `JellyfinCatalogService`** (2400 lines) and grade scan handlers for per-kind divergence.
- Lift Rating/Flags/Links/Files into capabilities; make per-kind capability membership registry-driven; unify Credits projection.
- `EntityGridToolbar` split; `EntityThumbnail` scrub extraction; lightbox rune; `EntityGridSection` migration; bespoke button/stepper/slider consolidation; add the planned `no-magic-codes` lint to lock the gains in.
