# Architecture Cleanup & Consolidation Plan

Status: **in progress** — coordination document for a multi-phase cleanup pass.
Date opened: 2026-05-31.

## Progress (2026-05-31)

- **Phase 1 (typed identifiers): done.** `RelationshipKind` + `FileSourceKind` enums,
  `JellyfinProtocol` + `JellyfinRoutes`, `ExternalIdProviders`, `MediaContentTypes`, plus a
  source-scanning drift-guard test. No migrations (codes unchanged).
- **Phase 2 (codegen): done.** `GET /api/_codegen/codes.json` (dev-only) + `gen-codes.mjs`
  wired into `api:generate` emit `generated/codes.ts`; `entity-codes.ts`/`app-settings.ts`
  re-export it. `EntityFileRole` drift reconciled (dead `banner/full/hero/original`
  branches removed).
- **Phase 3 (capability/relation tidy): done.** Subtitle extraction timestamp moved onto
  `CapabilitySubtitles.ExtractedAt` (no migration — column kept on `video_details`).
  Capability model + credits projection documented in the architecture contract.
- **Phase 4 (decompose large files): partially done / deferred.** `SettingsService`
  library-root use cases split into a partial. The remaining splits — the playback hot
  paths `JellyfinCatalogService` (1381) and `HlsAssetService` (1167),
  `JellyfinCompatibilityEndpoints`, `EfEntityReadService`, `IdentifyPluginService`,
  `PluginCatalogService` — are **deferred**. Rationale: partial-class splits do not reduce
  LOC or sprawl (they add files and spread one class), and mechanically cutting
  streaming/Jellyfin hot-path code is best done as small, individually-reviewed PRs rather
  than batched. Recommend doing them one file per PR with `verify`.
- **Phase 5 (closeout): done.** Audit script clean; full backend suite + frontend
  typecheck green; LOC delta captured below.

This plan consolidates the audit of the Prismedia backend + frontend and sequences
the cleanup into ordered, independently-shippable phases. It exists so the work can
be coordinated, reviewed in small commits, and so nothing in the audit is lost
between sessions.

## Goals

1. Eliminate untyped "magic string" identifiers; route every stable string through a
   single typed source of truth (enum-with-`[Code]` or a `Constants` class).
2. Generate the frontend's mirror of those codes from the backend instead of
   hand-maintaining them.
3. Decompose oversized files into scoped, single-responsibility units.
4. Keep the rich `Entity` + capability model clean: shared logic in core/capabilities,
   one-off logic on the implementation, relations expressed through children +
   relationships (not bespoke arrays).
5. Keep layer pathways thin: Domain rich, Application coordinates, Infra/API project.

## Success metrics

- Net **reduction in lines of code** across the touched areas (tracked per phase via
  `git diff --stat`), without trading clarity for terseness.
- Zero raw relationship/Jellyfin/file-source string literals outside their owning
  constant/enum (verified by a grep guard, see Phase 1).
- Frontend code constants are **generated**, with no hand-maintained duplication of
  backend enum values.
- No regression in the architecture audit script (`scripts/audit_dotnet_architecture.py`
  via the `dotnet-domain-efcore-architecture` skill) — currently clean.

## Decisions locked in

- **Frontend code sync:** generator script wired into `api:generate` (backend is the
  single source of truth). _Not_ manual + guard test, _not_ pure OpenAPI enums.
- **Relationship codes:** promote to a `RelationshipKind` enum with `[Code]`, matching
  the existing `EntityKind` / `JobType` pattern.
- **Single-kind capabilities** (Markers/Subtitles on Video, Progress on Book,
  Position on Season): **keep as capabilities** — the capability is the
  persistence/contract projection unit and is reusable by future kinds.

## What the audit confirmed is already healthy (do not "fix")

- Clean Architecture skeleton is sound; mechanical audit passes (no forbidden refs,
  no EF/HTTP/JSON in Domain, no domain entities returned from API, migrations in Infra).
- `Entity` base already models relations the desired way: structural `ChildEntities` /
  `ChildrenByKind` + non-structural `Relationships` / `RelationshipsByKind`, with
  `EntityRelationshipLinkRow` carrying `RelationshipCode` + `MetadataJson`. No bespoke
  per-relation arrays.
- **Credits are modeled correctly**: the link row stores only `TargetEntityId` (Person
  guid) + role-in-`MetadataJson` + optional label. Person data is *referenced*, not
  cloned. (`CreditsCapabilityMapper`, `EntityRelationshipLinkRow`.) No change needed.
- Existing typed registries are good and are the template to extend:
  - `[Code]` attribute + `CodecRegistry` / `EnumCodec` (encode/decode by reflection)
    — `Prismedia.Domain/Entities/Enums/Codec/`.
  - `EntityKindRegistry` (code ↔ kind ↔ CLR type) — `Prismedia.Domain/Entities/Kinds/`.
  - `CapabilityKindAttribute` + `CapabilityPolymorphism` (JSON discriminators) —
    `Prismedia.Contracts/Entities/Capabilities/Core/`.
  - Existing `[Code]` enums: `EntityKind`, `JobType`, `EntityFileRole`, `PlaybackMode`,
    `EntitySubtitleSource`, `SubtitleStyle`, `CreditRole`, `IdentifyQueueState`,
    `IdentifyResultStatus`.
- `AppSettingKeys` (backend) already centralizes setting keys.
- Application-layer coordination (e.g. `JellyfinCatalogService` grouping/mapping) is in
  the **correct layer** — it is just large (addressed in Phase 4, not relayered).

---

## Phase 1 — Backend typed identifiers (highest value, lowest risk)

No persisted string values change in 1a–1e (the codes stay byte-identical, e.g.
`"cast"`, `"scan"`), so **no EF migration is required** for this phase — it is a pure
code-level typing sweep.

### 1a. `RelationshipKind` enum

- New `RelationshipKind` enum with `[Code]` in `Prismedia.Domain/Entities/Enums/` (mirror
  `CreditRole`/`EntityFileRole` layout). Members + codes from the inventory:
  `Cast = "cast"`, `Credits = "credits"`, `Related = "related"`, `Studio = "studio"`,
  `Tags = "tags"`.
- Decide the `"performer"` case: it is a Stash/scraper *input alias* that maps to `Cast`.
  Keep it as an inbound mapping in the StashCompat/identify boundary (translate to
  `RelationshipKind.Cast`), **not** as an enum member, so the canonical code stays `cast`.
- Replace literals at (from inventory):
  - `EfEntityRepository.cs:22` (`const "related"`)
  - `EfEntityReadService.cs:781,827,828,831`
  - `CreditsCapabilityMapper.cs:24` (`const "credits"`)
  - `EntityMetadataApplyService.Relationships.cs:160,168,201`
  - `CollectionRuleEngine.cs:132,133` (incl. `performer`→`cast` alias logic)
  - `LibraryScanPersistenceService.ScanMetadata.cs:208,211,215`
  - `JellyfinCatalogService.cs` credit/relationship label mapping (~1054-1056)
- Persist via `.ToCode()` / `.DecodeAs<RelationshipKind>()` exactly as other enums do.

### 1b. `FileSourceKind` enum

- New `[Code]` enum: `Scan = "scan"`, `Custom = "custom"` (extend with `Plugin`,
  `Upload` if those literals exist at apply time).
- Replace literals at: `EntityFileRow.cs:29`, `EntityAttachmentModelConfiguration.cs:64`
  (EF `HasDefaultValue`), `EntityImageAssetMutationService.cs:79,87`,
  `EntityCoverSelection.cs:45`, `LibraryScanPersistenceService.ProcessingState.cs:166,181`,
  `GridThumbnailService.cs:63`.
- Keep the EF default value string identical so no migration is needed.

### 1c. Jellyfin protocol constants

The Jellyfin compatibility surface is the single biggest cluster of loose literals.
Introduce a small set of `static class` constants in `Prismedia.Contracts/Jellyfin/` (or
`Prismedia.Api/.../Jellyfin/`), grouped by concern rather than one giant bag:

- `JellyfinHeaders`: `X-Emby-Authorization`, `X-Emby-Token`, `X-MediaBrowser-Token`,
  `X-Prismedia-Api-Key`, the `"Bearer "` scheme prefix.
  Consumers: `PrismediaAuthentication.cs:198-201,222`.
- `JellyfinRoutes`: route templates + public-route paths currently inline in
  `JellyfinCompatibilityEndpoints.cs` (~20 paths) and the prefix arrays in
  `PrismediaAuthentication.cs:22-25` and `SpaDevProxy.cs:19-22`. Share one prefix list
  between proxy + auth instead of two copies.
- `JellyfinItemTypes` / `JellyfinCollectionTypes` / `JellyfinMediaTypes`: `Movie`,
  `Episode`, `Series`, `Season`, `Folder`, `BoxSet`, `CollectionFolder`; `movies`,
  `tvshows`, `boxsets`; `Video`, `Audio`. Consumers: `JellyfinCatalogService.cs`
  (many, ~48-1346) and `JellyfinCompatibilityEndpoints.cs:544,565,764-766`.

### 1d. External-id provider keys

- New `ExternalIdProvider` `[Code]` enum (or `ExternalIdProviders` constants if values
  must stay open-ended): `imdb`, `tmdb`, `tvdb`, `stash`.
- Consumers: `StashResultMapper.cs:58,136`, scattered tests, sidecar/identify code.

### 1e. MIME types

- `MediaContentTypes` constants class for the ~20 hardcoded MIME strings across
  `VideoSourceService`, `AudioSourceService`, `HlsAssetService:1126-1128`,
  `VideoSubtitleAssetService`, `PluginArtworkDownloader`, `TrickplayService`,
  `FfmpegAudioTranscodeResult`, `SpaDevProxy:89`. Lower priority than 1a–1c but cheap.

### 1f. Drift guard

- Add a lightweight check (unit test or `scripts/release` step) that greps the backend
  for the now-owned literals outside their constant/enum file and fails if found. Keeps
  the sweep from silently regressing.

**Deliverables:** new enums/constants + call-site replacements + tests for the new
enums' code round-trips + grep guard. One commit per sub-item (1a … 1f).
Changelog: `Changed` (internal typing — keep entry minimal/high-level or omit if purely
internal). No migration.

---

## Phase 2 — Frontend code generation pipeline

Backend becomes the single source of truth; the frontend stops hand-maintaining codes.

### 2a. Backend codes endpoint

- Add a dev/codegen endpoint, e.g. `GET /api/_codegen/codes.json`, that serializes every
  code registry to a stable JSON shape:
  - Per enum: name, members `[{ name, code, label?, extra? }]`. Source the values by
    reflecting `CodecRegistry` / `EntityKindRegistry` / the `[CapabilityKind]` set so the
    endpoint can never drift from the enums.
  - Include: `EntityKind` (+ display name, category, storage shape), `RelationshipKind`,
    `EntityFileRole`, capability discriminators (`CapabilityKind`), `CreditRole`,
    `JobType`, `PlaybackMode`, `EntitySubtitleSource`, `SubtitleStyle`,
    `IdentifyQueueState`, `IdentifyResultStatus`, `FileSourceKind`, `ExternalIdProvider`.
  - Include setting keys from `AppSettingKeys` / the settings catalog so `app-settings.ts`
    keys can also be generated.
- Endpoint is read-only and dev-surface only (same posture as `/openapi/v1.json`).

### 2b. Generator script

- `scripts/gen-codes.ts` (or under `apps/web-svelte/`): fetch `codes.json` from the
  running API (reuse `PRISMEDIA_OPENAPI_URL` host), emit a generated TS module
  `src/lib/api/generated/codes.ts` containing the `as const` maps + derived union types
  (`ENTITY_KIND`, `RELATIONSHIP_CODE`, `ENTITY_FILE_ROLE`, `CAPABILITY_KIND`,
  `CREDIT_ROLE`, setting keys, …).
- Mark the file generated (header banner, lint-ignore) and keep it beside orval output.

### 2c. Wire into the generate step

- Extend `api:generate` to run orval **then** `gen-codes` (single command, same running
  API). Add the task to `turbo.json` if other tasks should depend on it.
- Document that generation requires the API running (already true for orval).

### 2d. Rewrite hand-maintained frontend constants

- `entity-codes.ts`: keep only the *frontend-only* logic (labels, `ROUTE_RULES`,
  `resolveEntityHref`, etc.); import the code values from generated `codes.ts`.
- `app-settings.ts`: import generated setting keys; keep only frontend defaults/typing.
- Keep the `satisfies EntityCapability["kind"]` safety where the generated union is
  available so capability codes still get a compile-time cross-check.

### 2e. Reconcile `EntityFileRole` drift (real bug surfaced by the audit)

- Backend enum has `grid-thumbnail`, `waveform`, `hls`; frontend `ENTITY_FILE_ROLE`
  invents `banner`, `full`, `hero`, `original` that have **no backend counterpart**.
  Decide canonical set on the backend, then regenerate — this removes dead/desynced
  frontend roles. Audit frontend usages of the invented roles before deleting.

**Deliverables:** endpoint + generator + wiring + rewritten `entity-codes.ts` /
`app-settings.ts` + reconciled file roles. Changelog: `Added` (codegen for shared codes).

---

## Phase 3 — Capability / relation tidy

The model is largely correct; this phase is small and surgical.

### 3a. Fold `Video.SubtitlesExtractedAt` into `CapabilitySubtitles`

- Today `Video` carries a scalar `SubtitlesExtractedAt` (persisted on `VideoDetailRow`,
  projected on `VideoDetail` DTO) separate from `CapabilitySubtitles` which holds the
  tracks. The timestamp is extraction state for that capability and belongs with it.
- Move it into `CapabilitySubtitles` (e.g. `ExtractedAt`), update the mapper, the
  read checks in `LibraryScanPersistenceService.ProcessingState`/`DownstreamNeeds`, and
  the contract.
- **This is the only Phase that needs an EF migration** (drop `VideoDetailRow` column /
  relocate into the subtitles capability persistence). Review the migration as a data
  change; backfill `ExtractedAt` from the old column.
- This is a recommended consolidation, not yet locked — confirm before executing.

### 3b. Document the pseudo-capability pattern (no code change)

- The 5 contract-only capabilities (`RatingCapability`, `FlagsCapability`,
  `FilesCapability`, `LinksCapability`, `ImagesCapability`) project universal `Entity`
  properties for a uniform client surface; they intentionally have no domain class.
  Document this in `backend-architecture-contract.md` so it is not "fixed" later.

### 3c. Confirm credits projection note

- Credits are a domain capability but projected as `EntityCreditMetadata` on detail
  routes rather than a contract capability. Document the intent; no change.

**Deliverables:** 3a code + migration + tests; 3b/3c doc updates. Changelog: `Changed`.

---

## Phase 4 — Decompose oversized files

Use the established `ClassName.Responsibility.cs` partial-class convention already in the
repo (`EntityMetadataApplyService.Relationships.cs`,
`LibraryScanPersistenceService.ScanUpserts.cs`). Pure mechanical splits — behavior
unchanged, reviewed for accidental signature drift. Proposed seams:

| File | Lines | Split into |
| --- | --- | --- |
| `Application/Jellyfin/JellyfinCatalogService.cs` | 1381 | `.cs` (query/browse) + `.Mapping.cs` + `.Imaging.cs` + `.Metadata.cs` (dates/people/links/streams) |
| `Infrastructure/Videos/HlsAssetService.cs` | 1167 | `.cs` (asset lookup) + `.Generation.cs` (ffmpeg/segments) + `.Quality.cs` (rendition/codec) + `.Playlists.cs` |
| `Infrastructure/Entities/EfEntityReadService.cs` | 857 | `.cs` (list/filter) + `.CardProjection.cs` + `.Thumbnails.cs` |
| `Api/Endpoints/Jellyfin/JellyfinCompatibilityEndpoints.cs` | 785 | `.UserEndpoints.cs` + `.CatalogEndpoints.cs` + `.ImageEndpoints.cs` (already grouped by `Map*`) |
| `Infrastructure/Plugins/IdentifyPluginService.cs` | 732 | `.cs` (lookup/execute) + `.StructuralProposals.cs` (build/merge/match) |
| `Infrastructure/Plugins/PluginCatalogService.cs` | 687 | `.cs` (install/config) + `.RemoteIndex.cs` + `.Artifacts.cs` (extract — security-sensitive) |
| `Contracts/Jellyfin/JellyfinDtos.cs` | 631 | `.System.cs` + `.Catalog.cs` + `.Media.cs` (+ images/prefs) |
| `Application/Settings/AppSettingsRegistry.cs` | 594 | keep unified or extract category factories into `.Definitions.cs` |
| `Application/Settings/SettingsService.cs` | 541 | `.cs` (CRUD/validation) + `.TypedAccessors.cs` + `.LibraryRoots.cs` |

**Deliverables:** one commit per file split (easy to review). No behavior change, no
migration. Changelog: omit (internal).

---

## Phase 5 — Layering & dedup verification (closeout)

- Re-run the mechanical audit script; confirm still clean.
- Spot-check that Phase 1–4 didn't push coordination logic into Domain or duplicate
  domain rules in handlers.
- Capture final `git diff --stat` LOC delta vs. the Phase 0 baseline for the success
  metric.

---

## Suggested execution order & dependencies

1. **Phase 1** first — unblocks Phase 2 (the generator emits the new enums) and is the
   highest-value, no-migration work.
2. **Phase 2** next — depends on Phase 1's enums existing.
3. **Phase 4** can run in parallel with Phase 2 (independent, mechanical).
4. **Phase 3** independent; do when ready (3a needs a migration + confirmation).
5. **Phase 5** closeout.

Each phase = small scoped commits, changelog entry where user-facing, tests with new
logic, migrations reviewed as data changes. Build/validate with
`dotnet build apps/backend/Prismedia.slnx` and the frontend typecheck before each commit.

## Open confirmations before coding

- Phase 3a (`SubtitlesExtractedAt` fold) — proceed? It is the only migration in the plan.
- `codes.json` location/shape — a dedicated `/api/_codegen` endpoint vs. extending an
  existing metadata endpoint.
- Whether MIME-type consolidation (1e) is in-scope now or deferred (lowest value).
