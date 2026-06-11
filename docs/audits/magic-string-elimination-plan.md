I have all the verified context I need: `EntityKindRegistry.Video.Code` accessors exist, `EnumCodec`/`CodecRegistry` exist, the codes manifest reflects `[Code]` enums + `ExternalIdProviders` + `AppSettingKeys`, gen-codes.mjs has a fixed `ENUM_EXPORTS` allowlist, and tests use xUnit (no NetArchTest yet). Now I'll produce the plan.

# Magic-String Elimination & Prevention System

This plan turns the deduped inventory (~430 literals, ~190 families) into (1) a category→canonical-home table, (2) a phased elimination order, (3) concrete guardrails that make recurrence fail CI, and (4) the `CLAUDE.md` contract text. It is anchored to the real infrastructure already in the repo:

- **Backend codec**: `apps/backend/src/Prismedia.Domain/Entities/Enums/Codec/` (`EnumCodec.cs`, `CodeAttribute.cs`, `CodecRegistry.cs`, `CodecRegistryExtensions.cs`)
- **Kind registry**: `apps/backend/src/Prismedia.Domain/Entities/Kinds/EntityKindRegistry.cs` (exposes `.Video.Code`, `ToCode(EntityKind)`)
- **Jellyfin wire vocab**: `apps/backend/src/Prismedia.Contracts/Jellyfin/JellyfinProtocol.cs`
- **Manifest → codegen**: `apps/backend/src/Prismedia.Api/Codegen/CodesManifest.cs` → `/api/_codegen/codes.json` → `apps/web-svelte/scripts/gen-codes.mjs` → `apps/web-svelte/src/lib/api/generated/codes.ts`
- **Settings**: `apps/backend/src/Prismedia.Application/Settings/AppSettingKeys.cs` + `AppSettingsRegistry.cs`
- **Tests**: xUnit projects under `apps/backend/tests/*` (e.g. `CodecCompletenessTests.cs`) — no NetArchTest yet.

---

## 1. Category Table — Identifier Families → Canonical Home

Legend: **EXISTS** = canonical home already in repo (flag consumers only). **CREATE** = new source-of-truth to author. **CODEGEN** = must also be surfaced to `codes.ts` via the manifest.

### A. Already-canonical families (consumers must reference, do not invent)

| Family | Sample literals | Canonical home | Status | Frontend home |
|---|---|---|---|---|
| Entity kinds | `video`, `movie`, `studio`, `person`, `audio-track`, `music-artist`, `book`, `gallery`, `tag`, `collection`, `video-series`, `video-season`, `book-page` | `EntityKind` `[Code]` → `EntityKindRegistry.Video.Code` / `ToCode(EntityKind)` | EXISTS | `ENTITY_KIND` (codes.ts) via `$lib/entities/entity-codes` |
| Relationship codes | `tags`, `studio`, `cast`, `credits` | `RelationshipKind` `[Code]` | EXISTS+CODEGEN | `RELATIONSHIP_CODE` |
| Credit roles | `actor`, `director`, `creator`, `performer`(✗ no code), `artist`, `narrator` | `CreditRole` `[Code]` | EXISTS | `CREDIT_ROLE` |
| Job types | `scan-library`, `probe-video`, `auto-identify`, `bulk-identify`, `library-maintenance`, `noop` (~18) | `JobType` `[Code]` | EXISTS | `JOB_TYPE` |
| Playback modes | `direct`, `hls` | `PlaybackMode` `[Code]` | EXISTS | `PLAYBACK_MODE` |
| Entity file roles | `source`, `logo`, `poster`, `thumbnail`, `cover`, `backdrop`, `waveform`, `trickplay`, `sprite`, `preview` | `EntityFileRole` `[Code]` | EXISTS | `ENTITY_FILE_ROLE` |
| Subtitle source | `embedded`, `sidecar`, `generated`, `provider`, `upload`, `manual` | `EntitySubtitleSource` `[Code]` | EXISTS | `SUBTITLE_SOURCE` |
| Capability kinds | `dates`, `progress`, `flags`, `rating`, `technical`, `images`, `description`, `playback`, `subtitles`, `markers`, `files`, `position` | `CapabilityPolymorphism.DiscriminatorKinds` | EXISTS | `CAPABILITY_KIND` |
| Identify queue state | `search`, `proposal`, `done`, `deleted`, `error` | `IdentifyQueueState` `[Code]` | EXISTS | `IDENTIFY_QUEUE_STATE` |
| External-id providers | `tmdb`, `imdb`, `tvdb`, `anidb`, `stash` | `Contracts.Entities.ExternalIdProviders` consts | EXISTS | `EXTERNAL_ID_PROVIDER` |
| Setting keys | `scan.intervalMinutes`, `subtitles.preferredLanguages`, etc. | `AppSettingKeys` consts | EXISTS | `SETTING_KEYS` |
| Jellyfin media types | `Video`, `Audio`, `Subtitle` | `JellyfinProtocol.MediaTypes` | EXISTS | (new FE mirror, see §C) |
| File source kind | `scan` | `FileSourceKind` `[Code]` | EXISTS | `FILE_SOURCE_KIND` |

### B. New backend `[Code]` enums to CREATE (closed sets, no home today) → must be CODEGEN-surfaced

| Family | Literals | New canonical home (file) | Add to `ENUM_EXPORTS` in gen-codes.mjs |
|---|---|---|---|
| **Position codes** | `season`, `episode`, `absolute-episode`, `volume`, `chapter`, `page`, `track`, `sort` | `Prismedia.Domain/Entities/Enums/Navigation/PositionCode.cs` (`[Code]`) — replaces bare `string Code` on `CapabilityPosition`/`EntityPosition`; consumed by `EntityMetadataPositionRules.cs`, `LibraryScanPersistenceService.VideoBatch.cs`, Jellyfin mapping | `POSITION_CODE` |
| **Progress units** | `item`, `page`, `chapter`, `track`, `cfi` | `Prismedia.Domain/Capabilities/UserState/ProgressUnit.cs` (`[Code]`) — replaces free-string `CapabilityProgress.Unit` | `PROGRESS_UNIT` |
| **Date codes** | `release`, `air`, `birth`, `career-start`, `first-air` | `Prismedia.Domain/Capabilities/Shared/EntityDateCode.cs` (`[Code]`) | `ENTITY_DATE_CODE` |
| **Date precision** | `year`, `month`, `day`, `text` | `Prismedia.Domain/Capabilities/Shared/DatePrecision.cs` (`[Code]`) | `DATE_PRECISION` |
| **Book type** | `book`, `comic`, `manga`, `novel` | `BookType` (exists) — add to `ENUM_EXPORTS` | `BOOK_TYPE` |
| **Book format** | `image-archive`, `epub`, `pdf` | `BookFormat` (exists) — add to `ENUM_EXPORTS` | `BOOK_FORMAT` |
| **Job run status** | `queued`, `running`, `succeeded`/`completed`, `failed`, `cancelled` | `JobRunStatus` (exists) — add to `ENUM_EXPORTS`; fixes raw SQL literals in `JobQueueService.cs:228/235` | `JOB_RUN_STATUS` |
| **Engagement status** | `watched`, `read`, `completed`, `unwatched`, `in-progress`, `new` (+ alias table) | `Prismedia.Domain/Entities/Enums/Media/EngagementStatus.cs` (`[Code]`) — canonical set; alias map stays server-side in `EfEntityReadService.cs:385-400` | `ENGAGEMENT_STATUS` |
| **Sort keys** | canonical: `title`, `added`, `rating`, `position`, `random`, `kind`, `references`, `last-played` | `Prismedia.Domain/Entities/Enums/Query/EntitySortKey.cs` (`[Code]`); alias normalization stays in `EfEntityReadService.cs:187-192` | `ENTITY_SORT_KEY` |
| **Sort direction** | `asc`, `desc` | `Prismedia.Domain/Entities/Enums/Query/SortDirection.cs` (`[Code]`) | `SORT_DIRECTION` |
| **Hover/preview kind** | `none`, `sprite`, `image-sequence`, `trickplay` | `Prismedia.Domain/Entities/Enums/Media/ThumbnailHoverKind.cs` (`[Code]`) | `HOVER_KIND` |
| **Thumbnail meta-icon** | `video`, `audio`, `image`, `book`, `disc`, `duration`, `count`, `gallery`, `collection`, `person`, `studio`, `tag`, `calendar`, `chapter` | `Prismedia.Domain/Entities/Enums/Media/ThumbnailMetaIcon.cs` (`[Code]`) — unifies `ReferenceCountContributor.cs`, `EfEntityReadService.Thumbnails.cs`, FE `entity-thumbnail.ts` union | `THUMBNAIL_META_ICON` |
| **Visibility / NSFW mode** | `off`, `show` | `Prismedia.Domain/Entities/Enums/Settings/VisibilityMode.cs` (`[Code]`) — used by `AppSettingsRegistry.cs:52`, NSFW components | `VISIBILITY_MODE` |
| **Subtitle style** | `stylized`, `classic`, `outline` (reconcile vs codes.ts `{stylized,plain}`) | `SubtitleStyle` (exists, **divergent**) — fix members to `stylized/classic/outline`, regen | `SUBTITLE_STYLE` (already exported) |
| **Subtitle format** | `vtt`, `srt`, `ass`, `ssa` | `Prismedia.Domain/Entities/Enums/Media/SubtitleFormat.cs` (`[Code]`) | `SUBTITLE_FORMAT` |
| **Transcoder profile** | `Auto`, `Software`, `VideoToolbox`, `Vaapi`, `Nvenc`, `Qsv` | `Prismedia.Application/Videos/HlsTranscoderProfile` (exists as enum; ensure `[Code]`) | `TRANSCODER_PROFILE` |
| **Video range** | `SDR`, `DOVI`, `HDR10` | `VideoPlaybackRange.Sdr.VideoRangeType` (exists) — promote to `[Code]` enum `VideoRangeType` | `VIDEO_RANGE_TYPE` |
| **Stream method** | `direct`, `remux`, `transcode` | `Prismedia.Domain/Entities/Enums/Media/StreamMethod.cs` (`[Code]`) | `STREAM_METHOD` |
| **Plugin runtime** | `dotnet-process`, `stash-compat`, `python`, `typescript` | `Prismedia.Domain/Entities/Enums/Providers/PluginRuntime.cs` (`[Code]`) — replaces `DotnetPluginProcessRunner.RuntimeCode` literal re-spelling across `PluginIndexParser.cs`, `PluginCompatibilityResolver.cs`; mirror into `packages/plugins/src/types.ts` `pluginRuntimes` const | `PLUGIN_RUNTIME` |
| **Update-check status** | `available`, `current`, `unknown`, `development` | `Prismedia.Contracts/System/UpdateCheckStatus.cs` (`[Code]`) | `UPDATE_CHECK_STATUS` |
| **Release channel** | `dev`, `alpha`, `beta`, `release` | `Prismedia.Contracts/System/ReleaseChannel.cs` (`[Code]`) | `RELEASE_CHANNEL` |
| **Plugin auth status** | `ok`, `missing` | `Prismedia.Domain/Entities/Enums/Providers/PluginAuthStatus.cs` (`[Code]`) | `PLUGIN_AUTH_STATUS` |
| **Stat codes** | `images`, `tracks`, `pages`, `chapters`, `items`, `popularity` | `Prismedia.Domain/Capabilities/Metadata/StatCode.cs` (`[Code]`) | `STAT_CODE` |
| **File-entry kind** | `directory`, `file` | `Prismedia.Domain/Entities/Enums/Files/FileEntryKind.cs` (`[Code]`) — types `FileEntry.kind` (currently unbacked `string`); fixes `EfFilesPersistence.cs`, `LocalManagedFileStorage.cs`, FE `file-tree-state.ts` | `FILE_ENTRY_KIND` |
| **Classification system** | `manual`, `plugin` | `Prismedia.Domain/Entities/Enums/Providers/ClassificationSystem.cs` (`[Code]`) | `CLASSIFICATION_SYSTEM` |
| **Plugin action** | `search`, `lookup-id`, `lookup-url`, `cascade` | `Prismedia.Domain/Entities/Enums/Providers/PluginAction.cs` (`[Code]`) — used by `IdentifyPluginService.cs`, `IdentifyQueueService.cs`, `StashScraperManifestFactory.cs` | `PLUGIN_ACTION` |

### C. Backend-only constant classes to CREATE (not codegen — server-internal or Jellyfin-wire)

| Family | Literals | New canonical home |
|---|---|---|
| **API problem codes** (~30) | `not_found`, `entity_not_found`, `root_not_found`, `invalid_path`, `file_conflict`, `unknown_job_type`, `plugin_not_found`, `invalid_collection`, `setting_not_found`, `missing_api_key`, … | `Prismedia.Contracts/Api/ApiProblemCodes.cs` (static `const string`) — every `ApiProblem.Code` and `FilesService` thrower references it. FE matchers (`hidden-entity.ts`) reference a generated mirror (add `ProblemCodes` to manifest as a `ConstantEntry` group → `PROBLEM_CODES`) |
| **Cache subdir / asset roots** | `videos`, `images`, `book-pages`, `audio-tracks`, `hls`, `hls2`, `hlsv`, `/assets/`, `/custom/artwork/`, `/plugins/artwork/` | `Prismedia.Infrastructure/Media/AssetPaths.cs` — `LibraryMaintenanceJobHandler.cs`, `AssetPathService.cs`, `MaintenancePersistenceService.cs`, `HlsAssetService.cs`, `JellyfinImageFileService.cs` all reference it |
| **Video/audio codec ids** | `hevc`, `h265`, `h264`, `av1`, `vp9`, `vp8`, `aac`, `flac`, `mp3`, `opus`, … | `Prismedia.Contracts/Media/MediaCodecs.cs` — `HlsAssetService.Encoding.cs`, `VideoSourceService.cs`, `PlaybackInfoService.cs`, `AudioSourceService.cs` |
| **Containers** | `mp4`, `ts`, `mkv`, `matroska`, `webm`, `avi`, … | `Prismedia.Contracts/Media/MediaContainers.cs` |
| **ffprobe color transfer/primaries** | `arib-std-b67`, `smpte2084`, `bt2020` | `Prismedia.Application/Videos/FfprobeColor.cs` — `VideoPlaybackRangePolicy.cs`, `FfmpegToneMapping` |
| **Jellyfin protocol additions** | `File`, `Default`, `FileSystem`, `Full`, `VideoFile`, `AggregateFolder`, `root`, `Primary`, `SortName`, `Ascending`, `ParentId`/`Ids`/`Recursive`/`Limit`/`StartIndex`… (~16 query keys), `Playing`/`Playing/Progress`/`Playing/Stopped` | Extend `JellyfinProtocol.cs` with `MediaProtocols`, `MediaSourceTypes`, `LocationTypes`, `PlayAccess`, `VideoTypes`, `ItemQueryKeys`, `SortBy`/`SortOrder`, `DisplayPreferenceKeys`, `PlaybackEvents` nested classes. FE mirror `$lib/jellyfin/protocol.ts` (hand-mirrored, parity-tested in §3.5) |
| **Stash-compat vocab** | `sceneByURL`/`performerByName`/… (capability keys), `scrapexpath`/`scrapejson`/`script` (engines), `release`/`birth` (date keys), provider prefix `stash-` | Consolidate into `StashScraperDefinition.CapabilityKeys` (exists) + new `StashActionKinds`; mirror `packages/stash-compat/src/types.ts` `capabilityKeys`/`scraperActions` consts (declaration site — flag consumers in `StashCompatRunner.cs`, `StashScraperManifestFactory.cs`, `packages/plugins/src/stash-adapter.ts`) |
| **Plugin wire protocol** | `proposal`, `candidates`, `provider-tree`, protocol version `2`, env prefix `PRISMEDIA_PLUGIN_`, manifest filenames | `Prismedia.Infrastructure/Plugins/PluginProtocol.cs` |
| **Upload field names** | `rootId`, `targetPath`, `relativePaths`, `file`, `seriesId`, `seasonNumber`, `libraryRootId` | `Prismedia.Contracts/Files/UploadFields.cs` — shared with FE uploader |
| **Route prefixes** | `/api`, `/assets`, `/openapi`, `/api/_codegen` | `Prismedia.Api/RoutePrefixes.cs` — `PrismediaAuthentication.cs`, `SpaDevProxy.cs`, `Program.cs` |

### D. Frontend-only constant sets to CREATE (no backend concept)

| Family | Literals | New canonical home |
|---|---|---|
| **Collection rule operators** | `equals`, `not_equals`, `contains`, `greater_than`, `between`, `in`, `is_null`, … (15) | `packages/contracts/src/collections.ts` → `COLLECTION_OPERATOR` const + matching backend `Prismedia.Domain/Collections/CollectionRuleOperator.cs` `[Code]`; engine (`CollectionRuleEngine.cs:166-185`) and FE (`models.ts`, `ConditionBuilder.svelte`) both reference |
| **Collection rule fields** | `title`, `rating`, `tags`, `studio`, `resolution`, `codec`, … (24) | `CollectionRuleField` `[Code]` enum (codegen) |
| **Collection mode / cover / group op** | `manual`/`dynamic`/`hybrid`, `mosaic`/`custom`/`item`, `and`/`or`/`not` | `COLLECTION_MODE`, `COLLECTION_COVER_MODE`, `RULE_GROUP_OP` consts in `collections.ts` (+ backend `[Code]`) |
| **Filter-id catalog** | `flags:favorite`, `rating:min:`, `status:watched`, `technical:resolution:`, `book-type:`, `taxonomy:orphaned`, … | `$lib/entities/filter-ids.ts` — single builder/parser module; `entity-grid.ts` + `EntityGridFilterDrawer.svelte` both import (today the vocabulary is duplicated across producer/parser in one file) |
| **Video filter types** | `tag`, `performer`, `resolution`, `studio`, `codec`, `duration`, `ratingMin`, `played`, … | `$lib/prefs/filter-types.ts` → `VIDEO_FILTER_TYPE` (kills the triple-repetition in `videos-list-prefs.ts` and `series-list-prefs.ts`) |
| **Resolution / duration buckets** | `4K`/`1080p`/`720p`/`480p`, `lt300`/`300-900`/`900-1800`/`gte1800` | `$lib/entities/buckets.ts` (must match server option side in `EfEntityReadService.Thumbnails.cs:277` and `CollectionRuleEngine.cs:18`) |
| **Entity-grid view/sort runtime consts** | `grid`/`list`/`feed`, sort tuples, `asc`/`desc` | Export runtime `ENTITY_GRID_VIEW_MODE`, `ENTITY_GRID_SORTS`, `SORT_DIR` from `$lib/entities/entity-grid.ts` (types exist; no value consts) — collapses the 3 independent `asc/desc` declarations |
| **Entity flag keys** | `isFavorite`, `isNsfw`, `isOrganized`; flag codes `favorite`/`nsfw`/`organized` | `$lib/entities/entity-flags.ts` → `ENTITY_FLAG_KEY` + `FLAG_CODE` |
| **Section ids** | `studio`, `credits`, `stats`, `dates`, `technical`, `progress`, `positions`, `classification`, `sources`, `fingerprints`, `links` | `$lib/entities/section-ids.ts` → `SECTION_ID` (re-declared 5× inside `EntityDetail.svelte`) |
| **Metadata patch fields** | `title`, `description`, `urls`, `dates`, `rating`, `flags`, `tags`, `studio`, `credits`, `images`, … | `Prismedia.Contracts/Entities/MetadataPatchFields.cs` (`const`) — `EntityMetadataApplyService*.cs`, `AutoIdentifyRunner.cs`, `EntityMetadataPatchValidator.cs`; FE mirror codegen → `METADATA_PATCH_FIELD` |
| **Queue names** | `library-scan`, `media-probe`, `fingerprint`, `gallery-scan`, … | `packages/contracts/src/jobs.ts` `queueDefinitions[].name` is the FE source — export `QUEUE_NAME` const and reference everywhere; document as intentionally distinct from `JOB_TYPE` |
| **Match type / reader mode / reader flow** | `direct`/`related`, `paged`/`webtoon`, `paginated`/`scrolled` | `MATCH_TYPE`, `READER_MODE`, `READER_FLOW` consts in their declaring modules (`search/models.ts`, `book-reader-route.ts`) |
| **Storage-key namespace** | `prismedia:*` localStorage keys, `prismedia:` plugin auth prefix | `$lib/storage/keys.ts` → `STORAGE_KEY` builder + `prismediaKey()` helper |
| **Nav / icon slugs / route slugs** | `/movies`, `/series`, icon slugs | `resolveEntityBrowsePath` / `ROUTE_RULES` in `$lib/entities/entity-codes.ts` (exists) + typed `IconSlug` union in `packages/ui-svelte/src/navigation/app-shell-sections.ts` |

### E. Acceptable / do-not-flag (declaration sites & external vocab)

Already-correct references (`ExternalIdProviders.AniDb`, `EntityKindRegistry.AudioTrack.Code`, `MediaContentTypes.Pdf`, `ENTITY_GRID_ALL_KINDS`), parse regexes, CSS classes, ARIA, display text, external ffprobe/ffmpeg/Stash/Kodi field vocabularies at their single decode boundary, and W3C DOM key/`DataTransfer` tokens. These remain inline but get a `// external vocab` comment so the analyzer's suppression is auditable.

---

## 2. Elimination Order

Ordered by **safety × leverage**. Mechanical = pure reference swap, no behavior change, high literal count. Structural = needs a new type/migration/codegen first.

### Phase 0 — Build the rails (no consumer edits yet)
1. Author all **§B new `[Code]` enums** + **§C/§D constant classes**. Add new enums to `ENUM_EXPORTS` in `gen-codes.mjs` and extend `CodesManifest.Build()` to emit the new `ConstantEntry` groups (`ProblemCodes`, `MetadataPatchFields`). Run `pnpm api:generate` against the dev API → `codes.ts` grows the new consts.
2. Land guardrail scaffolding from §3 in **warn-only** mode (analyzer as `info`, arch test `[Fact(Skip)]`, ESLint rule as `warn`).
3. Fix the three **divergences** flagged in the inventory before any swap (they are latent bugs): `SubtitleStyle` (`{stylized,plain}` → `{stylized,classic,outline}`), `IdentifyResultStatus` `accepted`→`applied`, and the `performer` credit-role that maps to no `CreditRole` code (`StashResultMapper.cs:58`, `entity-detail-edit.ts:224`).

### Phase 1 — Mechanical, highest count, zero behavior change
4. **Entity kinds** (~120 occurrences across A) — swap bare `"video"`/`"studio"`/… for `EntityKindRegistry.*.Code` (backend) / `ENTITY_KIND.*` (frontend). Biggest single win; start with the `MapEntityKindRoutes` call family in `apps/backend/src/Prismedia.Api/Endpoints/*/...Endpoints.cs` and the route `+page.svelte` `kind={...}` props.
5. **Capability kinds** (`dates`, `progress`, `flags`, `rating`, `technical`, `images`, …) — FE swap to `CAPABILITY_KIND.*`; these are pure reads in `capabilities.ts` and route detail pages.
6. **Job types** (`jobs-dashboard.ts`, `run-catalog.ts`, settings sections) → `JOB_TYPE.*`.
7. **Playback modes / file roles / relationship codes / credit roles / subtitle source** → their generated consts.
8. **Jellyfin media types** (`Audio`/`Video`/`File`) → `JellyfinProtocol.MediaTypes.*` (backend) and the new FE mirror.

### Phase 2 — Structural, needs the Phase 0 types
9. **Problem codes** — replace every `ApiProblem` literal with `ApiProblemCodes.*`; point FE matchers at the generated `PROBLEM_CODES`. Removes cross-layer string coupling (`FilesService` ↔ `FilesEndpoints`).
10. **Position / progress-unit / date / engagement / sort** families — swap to the new `[Code]` enums; touches scan persistence, metadata-apply, Jellyfin mapping, and `EfEntityReadService` alias tables (keep alias normalization, only the canonical literal moves).
11. **Book type/format**, **job run status** (incl. raw SQL in `JobQueueService.cs`), **thumbnail meta-icon / hover kind** unification across `ReferenceCountContributor` + `EfEntityReadService.Thumbnails` + FE.
12. **Filter-id catalog + filter types + buckets** — extract `$lib/entities/filter-ids.ts` and route producer/parser/`EntityGridFilterDrawer` through it. Highest FE drift-risk cluster; do as one focused PR with the §3.6 grep test guarding it.
13. **Collection rule operators/fields/modes** — backend `[Code]` + FE consts; reconcile engine ↔ `models.ts` ↔ `ConditionBuilder`.
14. **Cache/asset paths, codecs, containers, stash-compat, plugin protocol, upload fields, route prefixes, storage keys, section ids, metadata patch fields**.

### Phase 3 — Flip guardrails to blocking
15. Promote analyzer to `error` for the touched families, un-skip the arch test, set ESLint rule to `error`, enable the CI parity check (§3.4) and grep test (§3.6). Land the §4 `CLAUDE.md` section.

Each phase is independently shippable and changelog-worthy under `Changed`.

---

## 3. Prevention System — Guardrails That Make Recurrence Fail CI

### 3.1 Roslyn analyzer: ban bare identifier literals (backend)
- **Mechanism**: New analyzer project `apps/backend/tools/Prismedia.Analyzers/` (`DiagnosticAnalyzer`, `Microsoft.CodeAnalysis.CSharp`). Rule **PRISM001 "Bare identifier literal"** flags a `LiteralExpressionSyntax` string whose value is a member-code of any `[Code]` enum or a value of a registered constant class (`ApiProblemCodes`, `JellyfinProtocol`, `AssetPaths`, `MediaCodecs`, `AppSettingKeys`). The analyzer reflects the same `CodeAttribute`/const surface `CodesManifest` does, so the banned-literal set is auto-derived — no hardcoded denylist. Suppress with `// prism-vocab: external` trailing comment (whitelisted decode boundaries) or `[SuppressMessage]`.
- **Plugs in**: referenced by every `src/*.csproj` via `<Analyzer>`; `dotnet build apps/backend/Prismedia.slnx` (already in the canonical workflow and Docker `pnpm release:check`) fails on PRISM001 at `error` severity. Set in `Directory.Build.props` `WarningsAsErrors`.

### 3.2 Architecture test: completeness + no-raw-literals (backend)
- **Mechanism**: New `apps/backend/tests/Prismedia.Architecture.Tests/` (xUnit, add `NetArchTest.Rules`). Three facts:
  - **Codegen completeness** (extends `CodecCompletenessTests.cs`): assert every `[Code]`-bearing enum either appears in `gen-codes.mjs` `ENUM_EXPORTS` **or** is on an explicit `[ServerOnly]` allowlist — a new enum with no codegen decision fails.
  - **No duplicate codes** across a registry; every `ApiProblem.Code` usage resolves to an `ApiProblemCodes` field (Roslyn semantic-model scan over the Api assembly).
  - **Jellyfin literals**: no string literal under `Prismedia.Application/Jellyfin/**` equals a `JellyfinProtocol.*` const value unless referenced through it.
- **Plugs in**: `dotnet test apps/backend/Prismedia.slnx`, run in CI and Docker `release:check`.

### 3.3 Close the OpenAPI codec-enum → string gap (orval emits typed enums)
- **Problem**: `[Code]` enums serialize as plain `string` over the wire (via `CodecJsonConverterFactory`), so OpenAPI shows `type: string` and orval emits `string`, which is why FE redeclares unions.
- **Mechanism**: Add an OpenAPI **schema transformer** in `Program.cs` (Microsoft.AspNetCore.OpenAPI `AddOpenApi(o => o.AddSchemaTransformer(...))`) that, for any property typed as a `[Code]` enum, emits `enum: [<codes...>]` + `x-enum-codes`. Configure orval (`apps/web-svelte/orval.config.*`) with `enumGenerationType: 'const'` so generated models become typed string-literal unions instead of `string`. The generated `codes.ts` remains the *value* source; orval models become the *type* source, and both derive from the same `[Code]` reflection.
- **Plugs in**: `pnpm api:generate` (already runs orval then `gen-codes.mjs`).

### 3.4 CI parity check: `codes.ts` matches the backend manifest
- **Mechanism**: New script `apps/web-svelte/scripts/check-codes.mjs` — fetches `/api/_codegen/codes.json`, regenerates an in-memory `codes.ts`, and `diff`s against the committed file; non-empty diff exits non-zero ("run `pnpm api:generate`"). Add `pnpm release:check` dependency or a dedicated `codes:check` task in `turbo.json`.
- **Plugs in**: the manual validation workflow + Docker build's `pnpm release:check`. Guarantees a backend enum edit that isn't regenerated breaks the build.

### 3.5 Frontend ↔ Jellyfin protocol parity test
- **Mechanism**: `apps/web-svelte/src/lib/jellyfin/protocol.parity.test.ts` (vitest) asserts the hand-mirrored `$lib/jellyfin/protocol.ts` values equal the backend `JellyfinProtocol` values exposed through a tiny dev `/api/_codegen/jellyfin-protocol.json` (add a `JellyfinProtocol` section to `CodesManifest`). Any wire-vocab drift fails `pnpm --filter @prismedia/web-svelte test`.

### 3.6 ESLint + grep guard: ban raw kind/code literals (frontend)
- **ESLint mechanism**: Custom rule `no-magic-codes` in a local plugin (`apps/web-svelte/eslint-rules/no-magic-codes.js`), wired into `apps/web-svelte/eslint.config.js`. It loads the value sets from `codes.ts` at lint time and reports any string literal equal to a known code that is **not** a property access on the corresponding const (e.g. flags `"video"` but allows `ENTITY_KIND.video`). Exemptions via `// eslint-disable-next-line no-magic-codes -- external vocab`. Set `error` in Phase 3.
- **Grep CI mechanism** (belt-and-suspenders, catches `.svelte` template attrs ESLint may miss): `scripts/no-raw-codes.sh` greps `apps/web-svelte/src` for `kind=["']video["']`, `state === "search"`, filter-id prefixes, etc., against a denylist built from `codes.ts`; runs as a `turbo` lint task. The filter-id catalog (§D) is specifically guarded here because its vocabulary lived in two halves of one file.

### 3.7 Codegen for the missing families
- BookType/BookFormat/JobRunStatus/EngagementStatus/PositionCode/ProgressUnit/EntityDateCode/SortKey/… are added to `ENUM_EXPORTS` (§B). ProblemCodes + MetadataPatchFields are added as new `ConstantEntry` groups in `CodesManifest.Build()` and emitted by `gen-codes.mjs`. After this, **every closed-set identifier the FE consumes has exactly one generated source**, and 3.4 keeps it honest.

### 3.8 PR template + pre-commit
- Add a "Identifier Discipline" checkbox to `.github/pull_request_template.md` ("New closed-set string? → `[Code]` enum + codegen + referenced, not inlined"). Optional lightweight `lefthook`/husky pre-commit runs `scripts/no-raw-codes.sh` on staged files.

---

## 4. Text to ADD to `CLAUDE.md` (symlinked `AGENTS.md`)

Insert as a new top-level section after **Data & Integration Rules**:

```markdown
## Identifier Discipline (Magic-String Contract)

Closed-set string identifiers are NEVER written as bare literals. Every kind code,
relationship/credit/file-role code, job type, capability key, setting key, problem
code, sort/filter/status/position/progress/date code, playback/stream mode, provider
or runtime id, image-asset/meta-icon kind, and Jellyfin wire scalar has exactly one
source of truth and is referenced from it.

### The source of truth
- Backend closed sets are `[Code("...")]` enums under
  `Prismedia.Domain/Entities/Enums/**` resolved via `EnumCodec`/`CodecRegistry`
  (e.g. `EntityKindRegistry.Video.Code`, `RelationshipKind.Tags.ToCode()`,
  `CreditRole.Director.ToCode()`).
- Cross-cutting constants live in dedicated static classes:
  `ApiProblemCodes`, `JellyfinProtocol`, `AssetPaths`, `MediaCodecs`,
  `MediaContainers`, `AppSettingKeys`, `MetadataPatchFields`, `UploadFields`,
  `RoutePrefixes`, `PluginProtocol`.
- The frontend consumes ONLY `src/lib/api/generated/codes.ts`
  (re-exported via `$lib/entities/entity-codes.ts`): `ENTITY_KIND`,
  `RELATIONSHIP_CODE`, `CREDIT_ROLE`, `ENTITY_FILE_ROLE`, `JOB_TYPE`,
  `PLAYBACK_MODE`, `CAPABILITY_KIND`, `SETTING_KEYS`, `EXTERNAL_ID_PROVIDER`,
  plus the codegen'd families (`POSITION_CODE`, `PROGRESS_UNIT`, `ENTITY_DATE_CODE`,
  `BOOK_TYPE`, `BOOK_FORMAT`, `ENGAGEMENT_STATUS`, `ENTITY_SORT_KEY`,
  `THUMBNAIL_META_ICON`, `PROBLEM_CODES`, …). `codes.ts` is generated — never edit it.

### Rules
1. Need a closed-set string? Find its `[Code]` enum / constant class and reference it.
   Do NOT retype the literal. The DECLARATION site (the `[Code]` attribute or the
   const) is the only place the literal text appears.
2. New closed set with no home? CREATE a `[Code]` enum (or constant class for
   non-domain wire vocab), then surface it to the frontend: add the enum to
   `ENUM_EXPORTS` in `apps/web-svelte/scripts/gen-codes.mjs` (or a `ConstantEntry`
   group in `CodesManifest.Build()`), and run `pnpm api:generate` with the dev API up.
   Never hand-maintain a parallel TS union of codes.
3. Frontend types for codes come from orval-generated models (typed enums) or
   `codes.ts` — never a hand-written `"a" | "b"` union that duplicates a backend set.
4. Jellyfin/Stash and other external wire vocab belongs in `JellyfinProtocol` /
   `StashScraperDefinition.CapabilityKeys` (and their FE mirrors), referenced — not
   retyped. External decode-boundary field names may stay inline ONLY at the single
   parse site, annotated `// prism-vocab: external`.
5. Problem/error codes: throwers and HTTP mappers both reference `ApiProblemCodes`;
   the frontend matches against generated `PROBLEM_CODES`, never English message text.
6. NOT identifiers (leave inline): user-facing display text, log/exception messages,
   CSS/Tailwind classes, ARIA labels, file paths, URLs, format strings, test
   descriptions, and standard DOM key/MIME tokens.

### Enforcement (these fail CI, do not bypass)
- Roslyn analyzer PRISM001 fails the build on a bare literal equal to a known code.
- `Prismedia.Architecture.Tests` assert codegen completeness and no duplicate/raw codes.
- `pnpm api:generate` must leave `codes.ts` unchanged in CI (`check-codes.mjs` parity);
  a regen diff means you forgot to run it.
- ESLint `no-magic-codes` + `scripts/no-raw-codes.sh` ban raw code literals in the
  Svelte app, including the entity-grid filter-id catalog (`$lib/entities/filter-ids.ts`).

When in doubt, grep for the literal: if it already lives in a `[Code]` enum or
`codes.ts`, reference that. If it does not but is a closed set, you are adding the
canonical home, not another copy.
```

---

**Key files referenced**: `apps/backend/src/Prismedia.Domain/Entities/Enums/Codec/{EnumCodec,CodeAttribute,CodecRegistry}.cs`; `apps/backend/src/Prismedia.Domain/Entities/Kinds/EntityKindRegistry.cs`; `apps/backend/src/Prismedia.Contracts/Jellyfin/JellyfinProtocol.cs`; `apps/backend/src/Prismedia.Api/Codegen/CodesManifest.cs`; `apps/web-svelte/scripts/gen-codes.mjs`; `apps/web-svelte/src/lib/api/generated/codes.ts`; `apps/backend/src/Prismedia.Application/Settings/AppSettingKeys.cs`; `apps/backend/tests/Prismedia.Domain.Tests/CodecCompletenessTests.cs` (extend); `apps/web-svelte/eslint.config.js` (new rule); new: `Prismedia.Contracts/Api/ApiProblemCodes.cs`, `Prismedia.Infrastructure/Media/AssetPaths.cs`, `$lib/entities/filter-ids.ts`, `apps/backend/tools/Prismedia.Analyzers/`, `apps/web-svelte/scripts/check-codes.mjs`.

---

## Progress Log

Branch `chore/design-audit-rails`. The repo already had a `ConstantsDriftGuardTests`
("Phase 1 constants consolidation" owning `JellyfinProtocol` headers + `MediaContentTypes`
MIME types) and `JellyfinProtocol` / `MediaContentTypes` constant classes — the rails
pattern was partially established. Building on it:

- **✅ Rail 1 — Problem codes (Phase 0 §C / Phase 2 step 9).** `ApiProblemCodes`
  (`Prismedia.Contracts/System/ApiProblemCodes.cs`) now owns all ~50 codes; 111 bare-literal
  call sites swapped across endpoints, `FilesService`/`FileOperationException`, and auth.
  Enforced by `ApiProblemCodeDisciplineTests` (fails on any bare `ApiProblem(`/`FileOperationException(`
  literal). Wire values unchanged. _Frontend `PROBLEM_CODES` codegen still TODO — needs the
  manifest + dev API (Phase 0 step 1)._

- **✅ Route classifier fix (sprint regression).** `SpaDevProxyTests` failed on the branch
  base: PR #22's case-insensitive Jellyfin matching swept the app's own lowercase routes
  (`/videos`, `/audio`, `/artists`) into the backend. Fixed with shape-aware matching
  (bare page + `/{kind}/{guid}` detail → SPA; PascalCase or sub-resource paths, incl.
  lowercase Infuse `/videos/{id}/stream`, → backend) and **unified the duplicated classifier**
  into `JellyfinRoutes.IsJellyfinRequest` (was copied in both `SpaDevProxy` and
  `PrismediaAuthentication`). Full backend suite green (853 tests).

- **✅ Rail 2 — Jellyfin image types.** `JellyfinProtocol.ImageTypes` (Primary, Backdrop,
  Logo, Thumb, Banner, Art, Disc, Box) now owns the 42 image-type literals across the
  `JellyfinCatalogService` partials. Enforced via `ConstantsDriftGuardTests` ownership map
  (the values are unambiguous — absent elsewhere in the backend). Value-preserving; 199 API
  tests green.

### What the rails work has surfaced about sequencing

The backend's **unambiguous internal** families are already largely consolidated by prior
"Phase 1 constants consolidation" work: `AppSettingKeys` (zero inline setting-key leaks),
`JellyfinProtocol`, `MediaContentTypes`, plus the codec families now run through
`EntityKindRegistry`/`RelationshipKind` etc. The remaining magic strings fall into two buckets:

1. **External wire vocab** (Jellyfin sort/query/location/media-source/video-type keys; the
   rest of the §C tables) — cleanly ownable + guardable with the `ConstantsDriftGuardTests`
   pattern, **no codegen needed**. Cheap, safe, incremental. (Rails 1–2 are this kind.)
2. **Ambiguous closed sets** (codec/container, image-asset-kind, position-code, sort-key,
   engagement-status, …) — these legitimately appear in multiple vocabularies (e.g. `"hevc"`
   is both an ffprobe spelling and a canonical code), so per this repo's own
   `ConstantsDriftGuardTests` philosophy they must be **`[Code]` enums validated by round-trip
   tests**, not source-scanned constant classes. That requires the dev-stack codegen rail
   (author enum → `ENUM_EXPORTS`/`CodesManifest` → `pnpm api:generate`) and pairs with the
   **OpenAPI codec-enum→typed-enum transformer** (the highest-leverage structural guardrail).

**Recommended next:** the dev-stack codegen rail (bucket 2 + OpenAPI typed enums) is the
biggest single win but needs running services; the wire-vocab rails (bucket 1) are safe to
keep grinding offline. Sequence per appetite for the dev-stack setup.

- **✅ Rail 3 (foundation) — OpenAPI codec-enum→typed-enum transformer.** `CodecEnumSchemaTransformer`
  (registered in `Program.cs`) emits `enum: [<codes>]` for any DTO property whose CLR type is a
  codec enum. Verified-correct by schema-walk tracing against the running dev API.

### ⚠️ Major scope finding (2026-06-08): the contracts are stringly-typed at the boundary

The OpenAPI transformer is necessary but **currently a no-op**, because the contract DTOs do
**not** use the codec enum types — every enum-shaped field (`JobRun.type/status/targetKind`,
`EntityKindCount.kind`, `EntitySubtitle.source/format`, `BookDetail.bookType`, …) is declared
`string`. Confirmed: **0** contract properties use a codec enum type today. So the wire format
is genuinely `string`, and the generated frontend models are `string` by construction — not
merely by a serialization quirk.

**Therefore the real "typed enums on the frontend" rail is a contract retype**, not just a
transformer. For each enum-shaped DTO field:
1. Retype the contract property `string` → the codec enum type (e.g. `JobRun.Type: JobType`).
   The existing `CodecJsonConverterFactory` keeps the wire value identical (the string code).
2. Fix the backend mapping/construction site that currently assigns a string (it must now
   pass the enum — usually a `Parse`/registry lookup, often deleting a hand-rolled string).
3. The transformer then emits the enum schema automatically → orval generates a typed union.
4. Fix frontend consumers (some get *simpler* — e.g. the jobs dashboard's invented BullMQ
   vocabulary in `lib/jobs/*` can be deleted in favor of the now-typed `JOB_TYPE`/`JobRunStatus`).

This is a sizable, careful, **per-DTO** effort (~10+ DTOs) with backend + frontend ripple each.
Highest-value first DTO: **`JobRun`** — it also eliminates the audit's worst frontend
magic-string offender (the hand-maintained jobs queue/status vocabulary).

- **✅ Enabler — `Prismedia.Contracts` now references `Prismedia.Domain`** (inward dependency on
  the dependency-free core). This is what lets DTOs carry `[Code]` value objects instead of
  strings; it unblocks every DTO retype below.
- **✅ Rail 3 — first DTO proven end-to-end: `JobRun`/`JobQueueCountDto`** (`Type`/`Status` →
  `JobType`/`JobRunStatus`). The OpenAPI transformer now emits typed enums → orval types
  `JobRun.type/status` as unions; `JOB_RUN_STATUS` added to `codes.ts`. Wire format unchanged
  (codec round-trip test); backend + frontend green; non-breaking. Regenerating also fixed
  **pre-existing client drift on main** (allowNsfw optionality, missing `navLayout*` models) —
  motivates the **`codes.ts`/generated-client parity CI check** (do next).

### The proven recipe (repeat per DTO)
1. Retype the contract field `string → <CodecEnum>` (add `using Prismedia.Domain.Entities;`).
2. Fix the backend mapping/construction site(s) that assign it — usually *delete* a `.ToCode()`
   (when the source is already typed) or `DecodeAs<T>()` a stored code.
3. If the enum isn't yet in `codes.ts`, add it to `ENUM_EXPORTS` in `gen-codes.mjs`.
4. Restart the dev API → `pnpm api:generate` → confirm the generated model is a typed union.
5. `svelte-check` + unit tests; fix any consumers (many *simplify* — e.g. delete invented vocab).

- **✅ Architecture decision — Contracts may reference Domain.** The enforced boundary
  (`InfrastructureBoundaryTests`) forbade `Contracts → Domain`, blocking typed DTOs. Resolved
  by **relaxing the boundary** (user decision): Contracts may carry `[Code]` value objects from
  the dependency-free Domain core; the tests now assert only that Contracts stays free of
  Infrastructure/API/EF/ASP.NET (its real "pure data layer" intent). _(The `JobRun` slice had
  briefly red-lit these tests — a verification gap; now resolved.)_
- **✅ Batch 1 — `BookDetail`, `CollectionContracts`, `EntityGroup.Code`.** `BookType`/`Format`
  → `BookType`/`BookFormat`; collection `Mode`/`CoverMode`/`Source` → their enums; `EntityGroup.Code`
  → `RelationshipKind?`. Mappers simplified (dropped `.ToCode()`); the collection command service
  no longer hand-parses mode strings; the Jellyfin mapping now compares `RelationshipKind` enums
  (retiring dead `"genres"`/`"studios"` branches). 853 backend tests + frontend 0 errors.

### Completed (2026-06-08)
- **✅ Batch 2 — `EntitySubtitle.Source` → `EntitySubtitleSource`.** Format/SourceFormat kept
  string (served-format constant + external ffprobe vocab). Caught 2 invalid fixtures.
- **✅ Batch 3 — the `EntityKind` mega-batch.** `EntityCard`/`EntityDetail`/`EntityGroup.Kind`,
  `EntityThumbnail.Kind`/`ParentKind`, `EntityKindCount.Kind`, and the collection `EntityType`
  fields → `EntityKind`. EF projections decode (`DecodeAs<EntityKind>`); mappers drop `.ToCode()`.
  Forced the **entire Jellyfin catalog service onto `EntityKind` switches/comparisons** (the
  audit's worst string-ID offender), retiring dozens of literals + dead branches. Backend 853
  tests, frontend 0 svelte-check errors, 223 unit tests. (`ByType` dict stays string-keyed —
  enum dict-key serialization is finicky; low value.)
- **✅ Guardrail — `pnpm api:check`** regenerates the client and fails on drift.
- **✅ Batch 4 — the identify/plugin/stash real-entity-kind DTOs.** `FileLinkedEntity.Kind`,
  `OrganizePlanItem.Kind`, `IdentifyEntitySnapshot.Kind`, `IdentifyQueueItem.EntityKind`,
  `IdentifyApplyProgress.CurrentKind` (+ the internal `IdentifyApplyProgressStep.Kind` /
  `ReportEntity` / `PluginEntityKindCompatibility.RequestKindFor` return) → `EntityKind`. EF/queue
  boundaries decode (`DecodeAs<EntityKind>`); the Stash runner compares `EntityKind` and only
  `.ToCode()`s at the proposal-string boundary. Frontend: regen typed the 4 HTTP-surface models;
  removed the now-redundant `as EntityKind` casts (FileDetailPane ×2, identify-store ×1) and typed
  the hand-rolled `identify-types.ts` `entityKind`/`currentKind` fields. **Key finding (corrects
  the original plan):** `EntityMetadataProposal.TargetKind` is **NOT** an `EntityKind` — the
  identify/proposal vocabulary is `EntityKind` ∪ `{"video-episode"}` (a leaf-episode distinction
  Prismedia maps to `Video` during structural binding; the matching/dedup logic depends on keeping
  it distinct). Forcing `EntityKind` there is semantically wrong, so `TargetKind` stays `string`
  for now — see the dedicated follow-up below. Backend 853 tests, frontend 610 + 0 svelte-check
  errors.

- **✅ Batch 5 — the `ProposalKind` `[Code]` enum for the identify vocabulary.**
  `EntityMetadataProposal.TargetKind` is now `ProposalKind` — a `[Code]` enum mirroring `EntityKind`
  code-for-code **plus** `VideoEpisode [Code("video-episode")]` (the provider-only leaf-episode
  token). One mapper, `ProposalKind.ToEntityKind()` (`VideoEpisode → Video`, identity otherwise),
  collapses it at the structural-binding boundary; `ToProposalKind()`/`IsRelationship()` round it
  out. Every `"video-episode"`/`AreCompatibleProposalKinds`/`StructuralKindKey`/`IsCompatibleStructuralKind`
  site now reasons in `ProposalKind`/`EntityKind` (the bare `"video-episode"` literals are gone from
  the proposal path; the one remaining pair lives in `IsKindCompatible`, the **API patch-boundary**
  expected-kind vocab — a separate string family that also carries `"video-movie"`). Frontend:
  `targetKind` is the generated `ProposalKind` union; the `as EntityKind` casts became
  `proposalKindToEntityKind()` (in `entity-codes.ts`, mapping `video-episode → video`).
  **Architectural change:** `CodecJsonConverterFactory` moved `Prismedia.Api → Prismedia.Infrastructure`
  so the plugin-process wire **and** the durable identify-queue JSON columns round-trip codec enums by
  code (they previously serialized bare `string`s; this also fixed a latent Batch-4 bug where the
  plugin wire serialized `EntityKind` as a numeric value). Domain test `ProposalKindTests` pins
  `ProposalKind = EntityKind ∪ {video-episode}` and the round-trip. Backend 864 tests, frontend 610
  + 0 svelte-check errors.

### Completed (2026-06-09) — the "Remaining future enum families" sweep
- **✅ `FileEntry.Kind` → `FileEntryKind` `[Code]` enum** (directory/file), plus
  `MediaFileIgnoreReason` for the scan-ignore row's reason codes. Contract, exclusion
  persistence API, storage adapters, and FE (`FILE_ENTRY_KIND`) all reference codes.
- **✅ `EntityThumbnail.HoverKind` → `ThumbnailHoverKind` `[Code]` enum** (none/sprite/
  image-sequence/trickplay). The FE hover discriminated union and every construction/
  comparison site reference `THUMBNAIL_HOVER_KIND` — the wire emits none/sprite; the
  client-derived image-sequence/trickplay modes share the same generated vocabulary.
- **✅ `ProgressCapability.Unit`/`Mode` → `ProgressUnit` + `ReaderMode?`** end-to-end
  (domain capability, EF mapper, contract, update request, service, Book domain, FE
  reader surfaces). `ReaderMode` gained `Scrolled`; the EPUB save path normalizes its
  internal paginated flow to the paged code; hydration decodes legacy stored values
  tolerantly (`TryDecodeAs`, e.g. old `"paginated"` rows). Killed the three hand-rolled
  `"paged" | "webtoon"` TS unions (ComicReader, reader route, book-entity-reader).
- **✅ Patch-boundary expected-kind vocab**: `IsKindCompatible` references
  `ProposalKind.VideoEpisode.ToCode()`; the producer-less legacy `"video-movie"` alias is
  a documented `LegacyMovieExpectedKind` const (`prism-vocab: external`).
- **✅ (finding, corrects the plan) `StatsCapability.Code` is an OPEN provider vocabulary,
  not a closed set** — there are zero internal writers; stat codes come exclusively from
  plugin metadata patches, are stored as-is, and the FE renders whatever arrives. Do NOT
  enum-ize (`StatCode` is dropped from §B); the two filter sites
  (`IgnoredStatCodes`, the FE bit-rate meta filter) are annotated `prism-vocab: external`.
- **✅ (verified, no change needed) `PluginEntitySupport.EntityKind`** is external manifest
  wire vocab decoded tolerantly at `PluginEntityKindCompatibility`, which already references
  `EntityKindRegistry` codes and `DecodeAs<EntityKind>` — the correct boundary pattern.
  Same reasoning keeps `ImageCandidate.Kind` a wire string decoded via `ImageKindRoleResolver`
  (`MediaImageKind` is the canonical home; the resolver is the single decode boundary).

### Remaining
The hand-rolled `apps/web-svelte/src/lib/api/identify-types.ts`
(a parallel copy of generated models) is a separate cleanup — fold into generated re-exports;
`identify-candidate-card.ts` keeps one `as EntityKind` cast on a generic `string` param (test-only
caller). 

### Remaining DTO work-list (re-verified 2026-06-09 against the actual contracts)
Earlier entries (`EntityCard.Kind`, `BookDetail`, `EntitySubtitle.Source`, collection modes)
landed in Batches 1–3. Still stringly-typed today, by likely difficulty:
- `IdentifyQueueContracts.State` (×2) → `IdentifyQueueState` (enum exists; pure swap).
- `EntityFile.Role` (FilesCapability) → `EntityFileRole` (enum exists).
- `EntityImageAsset.Kind` (ImagesCapability) → `EntityFileRole` — the FE already compares
  these items against `ENTITY_FILE_ROLE` codes; it is the file-role vocabulary, NOT
  `MediaImageKind` (which is the provider artwork-classification vocabulary).
- `OrganizeContracts.Status`, `UpdateCheckResponse.Status` → small `[Code]` enums (§B).
- `EntityPosition.Code` / `EntityDate.Code` / `EntitySource.Code` / `CreditPatch.Role` —
  capability codes fed partly by plugin patches; audit each for open-vs-closed before
  enum-izing (the stat-code finding above shows the plan's assumption can be wrong).
- `EntitySubtitle.Format`/`SourceFormat` — deliberately kept string (Batch 2 decision:
  served-format constant + external ffprobe vocab).
- `ApiProblem.Code` — stays string by design; the closed set lives in `ApiProblemCodes`.
- `HealthResponse`/`WorkerHealthResponse.Status` — trivial liveness strings; low value.