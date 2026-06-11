# Identify Flow Audit — 2026-06-08

Audit of the end-to-end identify pipeline (backend orchestration, plugin runtimes,
apply path, queue state machine, and frontend review surfaces) against three design
intents:

1. **One recursive same-shape walk.** Every entity is the same shape; identifying it
   resolves the **direct entity → its relations → its children**, and children walk the
   *exact same flow*, recursively, with no per-kind special-case branches.
2. **Uniform `EntityThumbnail` preview.** The "To Identify" target — and children,
   relationships, and candidates — should always render the shared `EntityThumbnail`
   with click-to-open-in-new-tab. No one-off per-kind rendering.
3. **A first-class two-mode plugin contract.** Plugins expose exactly two abilities —
   a **confident deterministic match** (ID/URL lookup → one high-confidence proposal)
   and a **confidence-scored string search** (title search → ranked candidates) —
   modeled *consistently across every runtime* (dotnet child-process and Stash-compat).

Method: 4 mapping passes + 7 dimension finders + adversarial per-finding verification
(54 of 58 findings confirmed against the real code) + an architect synthesis, all
cross-checked against a manual read of the core files.

---

## Executive summary

**The bones are good.** The proposal node (`EntityMetadataProposal`) is genuinely
uniform and self-recursive — it carries both `Children` and `Relationships` as
`IReadOnlyList<EntityMetadataProposal>` (`IdentifyProposals.cs:72-83`). `ProposalKind`
mirrors `EntityKind` code-for-code, the structural-vs-relationship split is a single
predicate (`ProposalKind.IsRelationship()`), the cascade gate is real kind metadata
(`EntityKindRegistry.EnumeratesIdentifyChildren`), and the backend apply *does* recurse
through structural children. The user's instinct — "the flow seems mostly good" — is
correct.

**The weaknesses are concentrated in five clusters:**

- **The two-mode contract is implicit and stringly-typed.** "Deterministic vs search"
  has no first-class home. It is inferred from `Patch == null`, encoded as bare action
  strings (`"search"`/`"lookup-id"`/`"lookup-url"`) with **no `[Code]` enum**, and
  *re-decided independently* in at least three places (`ResolveAction`, `GuessAction`,
  `AutoIdentifyRunner`) plus a queue-only `RequireChoice` post-filter. The two runtimes
  disagree on who picks the mode: the dotnet runner honors a plugin-declared
  `Type` discriminator; the Stash runner **ignores `request.Action`** and re-derives mode
  from input shape, hardcoding confidence.
- **The frontend has three parallel hand-rolled renderers instead of `EntityThumbnail`.**
  The target "context bar," the live-cascade children grid, and the candidate list are
  each bespoke — even though `EntityThumbnail` already handles every kind, placeholders,
  and `linkTarget="_blank"`, and a candidate→thumbnail mapper already exists but is dead.
- **A wide magic-string surface** in the exact flow that the repo's Magic-String Contract
  governs: identify actions, apply-progress lifecycle, provider image-kinds, and a
  frontend union duplicating `IDENTIFY_QUEUE_STATE`.
- **A handful of real correctness bugs** in the queue/cascade/apply state machine
  (double-apply, premature Accept-unlock on cascade retry, bulk-accept ignoring the
  cascade gate, an unenforced load-bearing `ProposalId`, no concurrency token).
- **Dead/parallel code** (`IdentifyResultStatus` table, an unused queue PUT endpoint,
  `IdentifyButton.svelte`, the candidate mapper).

**Counts:** 54 confirmed findings — 0 high, 21 medium, 33 low. The mediums are the
backlog; the correctness bugs are the only items that bite users today.

---

## Pillar 1 — One recursive same-shape walk

### What's already right
- `EntityMetadataProposal` is one self-recursive record with both `Children` and
  `Relationships` (`IdentifyProposals.cs:72-83`).
- The structural/relationship split is a single metadata predicate
  (`ProposalKindExtensions.IsRelationship()` → person/studio/tag), used by build, apply,
  and (mirrored) frontend via `EntityMetadataProposalTraversal`.
- The recursion gate is real metadata: `EntityKindRegistry.EnumeratesIdentifyChildren`
  (attribute-derived), consulted at `IdentifyPluginService.StructuralProposals.cs:67`.
- Backend apply genuinely recurses through structural children
  (`EntityMetadataApplyService.StructuralChildren.cs:50-57`).

### Gaps (medium)
- **Two runtimes model the contract differently.** `StashCompatRunner` hard-forks on
  concrete `EntityKind` (`is EntityKind.Video or EntityKind.Movie`,
  `StashCompatRunner.cs:51-54`) and ignores `request.Action`; the dotnet runner is
  manifest/metadata-driven. *This is the single clearest violation of "no per-kind
  special-case branches."*
- **Three+ hand-maintained kind sets encode overlapping facts and have already diverged:**
  `RelationshipOwnerKindCodes` (`Relationships.cs:12-23`),
  `AutoIdentifySelectorKinds.ByEntityKind` (`AutoIdentifySelectorKinds.cs:11-21`), and the
  frontend `IDENTIFY_CONTAINER_KINDS` literal set (`identify-review.ts:624-631`) — only
  `EnumeratesIdentifyChildren` is genuine metadata. e.g. `VideoSeason`/`BookVolume` are
  containers but not relationship owners; `Gallery`/`Image` are owners but not containers.
- **Movie↔Video equivalence is hardcoded in three places**
  (`PluginEntityKindCompatibility.cs:9-19`, `EntityMetadataApplyService.EntityResolution.cs:10-14`,
  `StashCompatRunner.cs:51,221-224`); the `EntityResolution.cs` copy also references a
  dead `"video-movie"` code with no producer.
- **Structural-position semantics fork on `VideoSeason`/`Video` in three places**
  (`EntityMetadataPositionRules.cs:16-24`, `StructuralProposals.cs:378-381`,
  `StructuralProposals.cs:514-521`) using bare `"seasonNumber"`/`"sortOrder"` literals.

### Gaps (low)
- **Relationships are leaf-applied; structural children recurse** — same record type, two
  walks (`ApplyRelationshipProposalsAsync` `Relationships.cs:277-311` vs
  `ApplyStructuralChildrenAsync` `StructuralChildren.cs:50-57`). **Decision: make this one
  recursive routine** — relations recurse like children (no leaf guard). See resolved
  decision 1.
- **Gallery images / book pages are never cascade-identified** (`Gallery`/`Image` lack
  `enumeratesIdentifyChildren`), even though Gallery owns relationships and is an
  auto-identify selector. **Decision: keep as leaves** and document the boundary; reconcile
  the kind sets so the leaf status is deliberate. See resolved decision 2.
- Image-role mapping forks on bare `image.Kind` strings inline (see Pillar 3 magic strings).

---

## Pillar 2 — A first-class two-mode plugin contract

### How the two modes are expressed today (the core problem)
The "confident deterministic match vs confidence-scored search" distinction is **never a
type**. It is inferred and re-expressed in *six* places, each with its own heuristics:

| Site | How it expresses the two modes |
|---|---|
| `ResolveAction` (`IdentifyPluginService.cs:246-273`) | bare `"search"`/`"lookup-id"`/`"lookup-url"` inferred from query/hint shape |
| `FallBackToSearchAsync` (`:350-377`) | recovery patch: re-run as `"search"` when a lookup returns nothing (doc comment admits it's a workaround) |
| `GuessAction` (`IdentifyQueueService.cs:506-515`) | **duplicate** action inference |
| dotnet runner (`DotnetPluginProcessRunner.cs:108-133`) | plugin-declared wire `Type == "proposal"/"candidates"`; builds a `null!`-stuffed candidates shell |
| Stash runner (`StashCompatRunner.cs:51-99`) | **ignores `Action`**, re-derives from URL/title presence + capability-name suffix; hardcodes confidence `0.9`/`0.7`; duplicate `null!` shell |
| `AutoIdentifyRunner` (`:85-185`) | `SelectConfidentCandidate` + `MeetsConfidenceBar` + `NormalizeConfidence` (tolerates 0–1 *and* 0–100) |
| Queue `SearchAsync` (`:143-178`) | `RequireChoice` downgrades a confident proposal back to a synthetic candidate |

### Findings (medium)
- **No `IdentifyAction` `[Code]` enum** for `search`/`lookup-id`/`lookup-url`(/`cascade`)
  — bare literals in 4+ files; `IdentifyPluginRequest.Action` and `IdentifyQueueItem.Action`
  typed as raw `string`. Direct Magic-String-Contract violation.
- **`EntityMetadataProposal.Patch` is declared non-nullable but the entire two-mode signal
  is `Patch == null`** (`DotnetPluginProcessRunner.cs:121` `Patch: null!`; discriminated at
  `IdentifyPluginService.cs:321`, `IdentifyQueueService.cs:523`). The most load-bearing
  signal in the system is a field the type contract swears is never null.
- **Stash advertises `person`/`gallery` support it never services**
  (`StashScraperManifestFactory.cs:24-26` vs the `Video/Movie` gate) — declared capability
  and runner behavior are out of sync, so the orchestrator routes a person to a runner that
  refuses it.

### Findings (low)
- The wire `Type` discriminator (`"proposal"`/`"candidates"`) is a bare string with no enum,
  and the two `null!` candidate-shell constructors are copy-pasted across both runners.
- Confidence/match-reason are inconsistent: dotnet passes provider values through; Stash
  injects fixed `0.9`/`0.7` for matches and **null** for search candidates — so
  `AutoIdentifyRunner.SelectConfidentCandidate` (which filters `Confidence is not null`) can
  *never* auto-select a Stash search result.
- `RequireChoice` participates only as a queue-layer post-filter, after a full
  deterministic+cascade lookup has already run and been discarded.
- A second, divergent Prismedia→Stash action/capability map exists in TS
  (`packages/plugins/src/types.ts:147-165`) alongside the C# one, covering different kinds.

---

## Pillar 3 — Uniform `EntityThumbnail` preview

`EntityThumbnail` (`thumbnails/EntityThumbnail.svelte`) is already the universal renderer:
per-kind placeholder via `iconForKind`, `placeholderGradient`, hover previews,
`linkTarget`, `selectable`/`selectMode`. **Only one identify surface uses it for the target
with open-in-new-tab** (`IdentifyTargetPreviewBody.svelte:99,108`). Everywhere else diverges.

### Findings (medium)
- **`IdentifyChildrenGrid.svelte` is a fully bespoke card grid** (`.child-tile` markup, raw
  `<img>`, hand-rolled select button, status overlays, ~60 lines CSS) — yet the *drilled*
  child review renders the same child concept via `EntityThumbnail` + `buildChildCard`
  (`IdentifyReviewChild.svelte:484-493`). **The same child entity is previewed two completely
  different ways depending on which screen you're on.**
- **`IdentifyReviewParent` and `IdentifyReviewChild` are near-duplicate ~600-line components**
  (identical state/selection logic, duplicated artwork CSS) — and already drift (parent's
  artwork aspect switch includes `still`, child's omits it).
- **The "context bar" target poster is a raw `<img>` with inline per-kind orientation logic**
  (`contextImageWide`/`coverIsSquare`) in parent, child, *and* choice — three copies.
- **`IdentifyReviewChoice` hand-rolls candidate rows and ignores
  `identifyCandidateToThumbnailCard`** (dead since only its test references it).
- **`VISUAL_KINDS` gate** (`IdentifyTargetPreview.svelte:18-27`) is a hardcoded literal set;
  person/studio/tag/audio get *no* target preview, and the body forks again on
  `videoSeries`/`videoSeason` to expand into episode rows.

### Findings (low)
- Synthetic credit/relationship/child cards set `entity.id = proposalId` and `linkable=false`,
  so a matched relationship can never open its real entity in a new tab (only the root target
  can) — even once `targetEntityId` is resolved.
- Per-kind artwork aspect-ratio and cover-selection logic duplicated as bare-string switches
  across parent/child/grid, already drifted.

---

## Correctness bugs (the only items that bite users today)

These are in the queue/cascade/apply state machine and are independent of the three pillars.

- **Premature Accept-unlock on cascade retry.** Cascade jobs run with `MaxAttempts = 3` and
  requeue on throw, but `RunCascadeAsync` clears `CascadeJobId` in a `finally` that runs on
  *every* attempt (`IdentifyQueueService.cs:273-275`). `cascadeRunning` flips false after
  attempt 1, **unlocking Accept while attempts 2–3 still stream children** → partial apply,
  and a later retry keeps writing to an already-applied row.
- **Double-apply.** `ApplyAsync` has no `row.State` guard and keeps `ProposalJson` populated
  after Done (`:324-387`), so a re-POST / double-click / bulk loop re-runs the full recursive
  write. The only protection is the UI optimistically removing the item.
- **Bulk accept ignores the cascade gate.** Single-item review disables Accept on
  `cascadeRunning`, but the dashboard bulk path filters only on `state === "proposal"`
  (`identify-store.svelte.ts:556,573`) → applies partial child trees mid-cascade.
- **`ProposalId` is load-bearing but unenforced.** Both runners emit `ProposalId: null!`; the
  cascade↔seed join and apply identity rely on `string.Equals` of a *plugin-authored* id
  (`:338,:406`). It works only because plugins happen to derive a stable id; a plugin emitting
  `Guid.NewGuid()` per call would make children silently never persist (the throw is swallowed
  by `SaveProposalSafelyAsync`).
- **No optimistic-concurrency token** on `IdentifyQueueItemRow` (contrast `job_runs`, which
  uses `xmin`); a long-lived cascade and live HTTP apply/delete/search write the same row from
  different contexts with only TOCTOU state-checks.

---

## Dead / parallel code

- `IdentifyResultStatus` enum + `IdentifyResultRow` + `IdentifyResults` DbSet — mapped and
  exported to `codes.ts` as `IDENTIFY_RESULT_STATUS`, but **never read/written** in the live
  flow (which uses `IdentifyQueueState`).
- `SaveIdentifyQueueProposal` PUT endpoint + generated client wrapper — **no UI caller**;
  in-progress children persist via the in-process cascade sink, not this endpoint.
- `IdentifyButton.svelte` — no importers; duplicates `use-identify-detail-action`.
- `identifyCandidateToThumbnailCard` — purpose-built unifier, referenced only by its test.

---

## Target architecture

A single recursive walk, a declared two-mode contract, and one thumbnail renderer.

### Node shape & contract
- `EntityMetadataProposal.Relationships` becomes a **required, non-nullable** list co-equal
  with `Children` (delete all `?? []` guards).
- `EntityMetadataProposal.Patch` becomes **`EntityMetadataPatch?`** — null is the *honest,
  first-class* "this is search candidates, not a match" signal. Delete `null!` shells.
- New `IdentifyAction` `[Code]` enum (`Search`/`LookupId`/`LookupUrl`); type
  `IdentifyPluginRequest.Action`, `PluginEntitySupport.Actions`, and the queue row/contract
  as it. Single `ResolveAction` is authoritative; `GuessAction` is deleted; `RequireChoice`
  folds into `ResolveAction` (forces `Search`).
- New `IdentifyResultKind` (`Proposal`/`Candidates`) and `IdentifyApplyState`
  (`Running`/`Succeeded`/`Failed`) `[Code]` enums; two factories
  `IdentifyPluginResponse.Match(...)` / `.Candidates(...)` replace both `null!` shells.
- New `MediaImageKind` `[Code]` enum + one `ImageKindRoleResolver.RoleFor(...)` consumed by
  every artwork path.

### Two-mode contract, uniform across runtimes
- Every runner **honors `request.Action`**. `StashCompatRunner` loses the `Video/Movie` gate
  (kind eligibility comes from the synthesized manifest's `Supports`); `sceneByName` is gated
  on `Action == Search`; search candidates get a real confidence via a shared
  `TitleSimilarityScorer` so both runtimes feed `AutoIdentify` the same way. **Implement the
  `performerByName`/`performerByURL`/`galleryByURL` scrape paths** so the advertised person
  and gallery support is real (resolved decision 3).

### One recursive apply walk
- Collapse `ApplyStructuralChildrenAsync` + `ApplyRelationshipProposalsAsync` into one
  `ApplyNodeAsync(entity, node, ApplyContext)`: apply patch+images, walk relationships, walk
  children — all via the same routine, threading `selectedFields`/`selectedImages` so children
  honor selection (today they don't — `EntityMetadataApplyService.cs:252` passes no selection).
- One `FindOrResolveEntityAsync` (external-id-first, then **one** normalized-title rule) for
  both structural children and relationship targets — fixes the title-only relationship match
  and the Ordinal-vs-`ToLower()` inconsistency.

### Kind metadata as the single source
- Add `ownsRelationships` (and a structural-children/selector facet) to
  `EntityKindMetaAttribute`; delete `RelationshipOwnerKindCodes` and the
  `AutoIdentifySelectorKinds` literal map. Emit `EnumeratesIdentifyChildren` +
  `ownsRelationships` + the relationship-kind classification into `codes.ts` so the frontend
  drops `IDENTIFY_CONTAINER_KINDS` and `isRelationshipKind`.

### One thumbnail renderer
- Delete `IdentifyChildrenGrid`; render root children via the same
  `structuralChildProposals` + `childCard` + `EntityThumbnail` path as the child view, with
  cascade status in `EntityThumbnail`'s `custom.bottomLeft` badge.
- Merge `IdentifyReviewParent` + `IdentifyReviewChild` into one `IdentifyReviewProposal` with a
  `mode: 'root' | 'child'` prop controlling only nav/apply chrome.
- Replace the three context-bar `<img>` blocks with one `IdentifyContextPoster`
  (`EntityThumbnail mediaOnly linkable={false}`).
- Render candidates via `EntityThumbnail` + `identifyCandidateToThumbnailCard` (retire the
  hand-rolled row). Delete `VISUAL_KINDS`; preview every kind; drive series/season expansion
  from `EnumeratesIdentifyChildren`.
- When a card has a real `targetEntityId`, set the real id + `linkTarget="_blank"` so matched
  relationships/children open in a new tab uniformly.

---

## Suggested sequencing

Ordered low-risk → higher-risk; each is an independent, committable slice.

1. **Correctness fixes** — Accept-unlock on retry, double-apply guard, bulk-accept cascade
   gate. *(Bugs; do first.)*
2. **`IdentifyAction` `[Code]` enum** (+ codegen) — collapse `ResolveAction`/`GuessAction`,
   type the row/contract. *(EF migration to normalize the `action` column.)*
3. **`IdentifyResultKind` + `IdentifyApplyState` enums.**
4. **`MediaImageKind` enum + `ImageKindRoleResolver`.**
5. **Kind-metadata facets** (`ownsRelationships`, container/selector) + codegen; delete the
   parallel sets.
6. **Unified `FindOrResolveEntityAsync`** (external-id-first, one title rule).
7. **Single `ApplyNodeAsync`** + thread selection through children.
8. **Fold `RequireChoice` into `ResolveAction`.**
9. **Metadata-driven `StashCompatRunner`** (+ `TitleSimilarityScorer`; **implement**
   `performerByName`/`performerByURL`/`galleryByURL` scrape paths — decision 3).
10. **Nullable `Patch` + non-nullable `Relationships` + response factories.**
11. **Delete dead code** (`IdentifyResultStatus` table, PUT endpoint, `IdentifyButton`).
12. **Frontend collapse** — delete `IdentifyChildrenGrid`, merge parent/child into
    `IdentifyReviewProposal`, `IdentifyContextPoster`, metadata-driven preview, candidate
    `EntityThumbnail`, real-id `linkTarget`.
13. **`AutoIdentifySelectorKinds` from metadata.**
14. **Type `IdentifyQueueItem.State`/`.Action` as enums on the wire; drop the
    `identify-types.ts` duplicated unions.**

### Risks to watch
- EF migration on the `action` column must normalize existing rows before the codec applies.
- Merging the two review components is the highest-risk frontend change — do it as a pure
  refactor (extract shared body first), guarded by the existing review tests.
- Typing `IdentifyApplyProgress.State` as an enum requires the codec converter on the
  SSE/progress serializer path, or the frontend starts receiving `Running` vs `running`.
- Adding `TitleSimilarityScorer` makes some Stash search results auto-applicable — needs a
  conservative threshold.

---

## Product decisions (resolved 2026-06-08)

1. **Relationship recursion → FULLY RECURSIVE.** Relations (person/studio/tag) are *not*
   terminal leaves. `ApplyNodeAsync` walks a relation node's own `Relationships` and
   `Children` via the same recursive routine as a structural child — relations and children
   are one uniform walk. *Implication to record for implementation:* the apply walk handles
   this cleanly once it's one routine, but **populating** a relation's subtree depends on
   (a) providers returning nested relationship structure in the proposal, and (b) the
   relationship target entities (person/studio/tag) being independently identifiable. The
   flat-link domain model has no schema for "a studio's parent studio" / "a performer's
   aliases" today, so this is the more ambitious choice: the *walk* becomes uniform now;
   *nested relation data* arrives as providers/domain support it. No truncation guard in
   `ApplyNodeAsync`.
2. **Gallery/book cascade → KEEP AS LEAVES.** Galleries/images and book chapters/pages stay
   non-containers (`enumeratesIdentifyChildren` unchanged). Encode "gallery images and book
   pages are intentionally not cascade-identified" as an explicit documented rule (a 500-image
   gallery cascade is out of scope). Reconcile the divergent kind sets so Gallery isn't a
   relationship-owner/selector *and* a non-container by accident — the leaf status is
   deliberate, not a gap.
3. **Stash person/gallery → IMPLEMENT.** Build the `performerByName` / `performerByURL` /
   `galleryByURL` identify paths so the advertised manifest support is real. Replace the
   `is EntityKind.Video or Movie` gate with a manifest-`Supports`-driven check, and honor
   `request.Action` (Phase 9). Do not merely drop the advertisement.
4. **Orphan policy.** When a re-identify returns fewer children than before, existing local
   children keep stale metadata. *Open:* make "never orphan structural children" an explicit
   documented rule, or add reconciliation. (Not yet decided — revisit during Phase 7.)

## Status

**Audit-only as of 2026-06-08.** Implementation paused at the user's direction; they will
review this document and direct which slice to start. Recommended first slice when resumed:
the **correctness fixes (Phase 1)**, then the **two-mode contract (Phases 2, 8, 9)** and the
**uniform `EntityThumbnail` collapse (Phase 12)**.
