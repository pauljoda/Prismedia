# Identify Flow Redesign — Implementation Writeup

## Problem

The current identify flow has two parallel UIs that don't share state:

1. **`IdentifyButton.svelte`** (2465 lines) — A modal triggered from entity detail pages (videos, series, books). Contains its own provider selection, identify API calls, proposal review (fields, images, credits, tags, cascade children), lightbox, and apply logic. Everything lives in one massive component.

2. **`/identify` route page** (747 lines) — A bulk-oriented page with entity listing, provider management, bulk identify sessions, and a slide-out review drawer. Has its own separate review UI that's simpler than the modal version.

Neither shares proposals, selection state, or providers with the other. Navigating away from either loses all progress. There's no way to queue multiple entities for review or walk through results sequentially.

---

## Proposed Architecture

### Route Structure

| Route | Purpose |
|---|---|
| `/identify` | Queue index — entity browser + review queue with state badges |
| `/identify/[entityId]` | Full-screen review page for a single entity (GUID-based) |

**Query params on `/identify/[entityId]`:**
- `?returnId=<guid>` — Arrived from entity detail. Hides queue nav and "Accept & Next". On accept, navigates back to the entity via `resolveEntityHrefById(returnId)`.
- `?provider=<id>` — Optional pre-selected provider to auto-start search.

### Root-Level Queue Store

A new `IdentifyQueueStore` class provided via Svelte context in the root `+layout.svelte`. Survives navigation across the entire app.

**Item state machine:**
```
not-searched → pending-choice → pending-review → complete
                     ^                ^
                     +-- re-search ---+
```

- **`not-searched`** — Entity added to queue, no identify call made yet
- **`pending-choice`** — Search returned multiple candidates, user must pick one
- **`pending-review`** — Proposal cached with full field/image/credit/tag/cascade selections
- **`complete`** — Proposal applied to entity

**Store responsibilities:**
- Hold all queued items with their state, provider, candidates, proposal, and selection state
- Kind filtering (show only videos, only series, etc.)
- Queue navigation helpers (next/previous pending item from current position)
- Proposal and selection caching — navigating away and back restores cached state instantly
- Images stay as URL references in the proposal; only downloaded by the backend on `applyIdentifyProposal()`

### Component Extraction

The 2465-line `IdentifyButton` modal would be decomposed into focused, reusable components:

| Component | Responsibility |
|---|---|
| `IdentifyReviewSurface` | Full review UI: fields, tags, studio, credits, artwork, children (seasons/episodes), cascade relationships, lightbox. Prop-driven, no API calls. |
| `IdentifyProviderBar` | Provider strip showing installed providers with auth state badges and active selection |
| `IdentifySourcePicker` | Candidate card list with manual search box for the pending-choice state |
| `IdentifyQueueNav` | Prev/next arrows + position indicator (e.g., "3/12") for queue traversal |
| `IdentifyEntityContext` | Entity thumbnail + title + kind badge + file name context bar |

All existing utility functions in `identify-review.ts` (`buildProposalForApply`, `groupProposalRows`, `reviewChildProposals`, selection logic) would be reused unchanged.

### Queue Index Page (`/identify`)

Two sections:

**Entity Browser (top):** Kind tabs (Movies, Series, Books, Galleries, Images) with search. Lists unidentified entities with "Add to Queue" buttons. Multi-select for batch queueing. Powered by existing `fetchIdentifyEntities(kind, search?)`.

**Review Queue (bottom):** All queued items with state badges. Kind filter chips to narrow view. Each row shows entity title + kind + state badge; clicking navigates to `/identify/[entityId]`. Mass actions: "Identify All" runs bulk identify for all not-searched items, "Clear Done" removes completed items.

### Review Page (`/identify/[entityId]`)

Full-screen page with state-driven rendering:

```
+-----------------------------------------------------+
| ProviderBar          |  QueueNav (< 3/12 >)  | Back |  <- top bar
|-----------------------------------------------------|
| [Thumb] file-name.mkv  .  Video  .  TMDB match     |  <- entity context
|-----------------------------------------------------|
|                                                     |
|              State-specific content                  |  <- scrollable body
|                                                     |
|-----------------------------------------------------|
| [Skip]               [Accept & Next]  [Accept]      |  <- sticky footer
+-----------------------------------------------------+
```

**State: `not-searched`** — Auto-selects first runnable provider and fires search. Shows spinner. If no providers, shows setup prompt.

**State: `pending-choice`** — Manual search box + candidate cards (poster, title, year, overview). Clicking a candidate re-runs identify with that candidate's external IDs, transitioning to pending-review.

**State: `pending-review`** — `IdentifyReviewSurface` renders the full review. "Other matches" link to go back to pending-choice. All selections cached in queue store.

**State: `complete`** — Success confirmation with "Next in Queue" or "Back to Queue" link.

**Footer actions:**
- **Queue mode (no `returnId`):** "Skip" (advance without applying), "Accept & Next" (apply + advance), "Accept" (apply + return to queue index)
- **Return mode (`returnId` present):** Only "Accept" (apply + navigate back to entity detail) and "Cancel" (back without changes)
- **Last item in queue:** "Accept & Next" hidden

### IdentifyButton Replacement

The 2465-line modal becomes a ~50-line navigation button:
```svelte
<button onclick={() => goto(`/identify/${entityId}?returnId=${entityId}`)}>
  <ScanSearch /> Identify
</button>
```

All consumers (videos/[id], series/[id], books/[id]) keep the same prop interface — unused props (`existingCreditNames`, `existingTags`, `onApplied`) are accepted but ignored since the review page handles everything.

---

## Entry Points

### From Entity Detail
1. Click "Identify" button
2. Navigates to `/identify/{entityId}?returnId={entityId}`
3. Review page loads in return mode — no queue nav, no "Accept & Next"
4. On accept: `resolveEntityHrefById(returnId)` resolves the route and navigates back

### From Queue Index
1. User adds entities to queue via entity browser
2. Optionally runs "Identify All" for bulk search
3. Clicks a queue item to navigate to `/identify/{entityId}`
4. Queue nav arrows traverse filtered items from the store

---

## Files

### New
- `apps/web-svelte/src/lib/stores/identify-queue.svelte.ts` — Queue store class
- `apps/web-svelte/src/routes/identify/[entityId]/+page.svelte` — Review page
- `apps/web-svelte/src/lib/components/identify/IdentifyReviewSurface.svelte` — Review UI
- `apps/web-svelte/src/lib/components/identify/IdentifyProviderBar.svelte` — Provider strip
- `apps/web-svelte/src/lib/components/identify/IdentifySourcePicker.svelte` — Candidate picker
- `apps/web-svelte/src/lib/components/identify/IdentifyQueueNav.svelte` — Queue navigation
- `apps/web-svelte/src/lib/components/identify/IdentifyEntityContext.svelte` — Entity context bar

### Modified
- `apps/web-svelte/src/routes/+layout.svelte` — Add `provideIdentifyQueue()`
- `apps/web-svelte/src/routes/identify/+page.svelte` — Rewrite as queue index
- `apps/web-svelte/src/lib/components/IdentifyButton.svelte` — Replace with navigation button

### Reused Unchanged
- `apps/web-svelte/src/lib/components/identify-review.ts` — All utility functions
- `apps/web-svelte/src/lib/entities/entity-route-resolver.ts` — `resolveEntityHrefById()`
- `apps/web-svelte/src/lib/api/identify.ts` — All API functions
- No backend changes needed

---

## Implementation Sequence

1. **Queue Store + Components** — Create the store, extract/build the 5 UI components, provide store in root layout
2. **Review Page** — Build `/identify/[entityId]` composing all components with the state machine
3. **Queue Index** — Rewrite `/identify` as entity browser + queue view
4. **Replace IdentifyButton** — Slim navigation button, update consumers
5. **Polish** — Keyboard navigation, mobile responsive, Prism Noir Luxe styling, cleanup
