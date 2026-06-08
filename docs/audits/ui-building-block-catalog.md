I now have a complete, grounded picture. Writing the plan.

# Prismedia Shared UI Building-Block System

A canonical catalog, composition kit, and migration map to make future pages composable from existing blocks with near-zero bespoke code. The north star mirrors the backend's canonical-identifier discipline (`EnumCodec`/`[Code]`/`CodecRegistry`): **one declaration site per concept, every consumer references it.**

The single most damning data point from the audit: **96 `.svelte` files import `@prismedia/ui-svelte`, but only 2 import the `$lib/components/forms` barrel.** Form-control primitives exist and are correct — they are simply un-adopted, so 52 raw-control files re-hand-roll inputs/selects/chips. The system below is mostly a *consolidation + adoption* effort, not a green-field build.

---

## 1. Canonical Building-Block Catalog

The definitive set every page should compose from. Status legend: **EXISTS** (use as-is), **CONSOLIDATE** (multiple competing impls → merge into the named winner), **NEW** (must be built).

### Form Controls (one per concept)

| Block | Status | Canonical home | Notes / competing impls to fold in |
|---|---|---|---|
| `TextInput` (bare) | **EXISTS** | `packages/ui-svelte/src/primitives/TextInput.svelte` | The styled base. `textInputVariants` already exported. |
| `TextField` (labelled) | **EXISTS** | `apps/web-svelte/src/lib/components/forms/TextField.svelte` | Wraps `FormField` + input. Under-adopted. |
| `Select` | **CONSOLIDATE** | `packages/ui-svelte/src/primitives/Select.svelte` | Raw `<select>` in `collections/ConditionBuilder.svelte:270-398` must use this. |
| `SearchSelect` / Combobox | **CONSOLIDATE** | `apps/web-svelte/src/lib/components/forms/SearchSelect.svelte` | **High leverage.** `SearchSelect`, `TagSelect`, `EntityPicker` independently re-implement the same `role=listbox` + search-input + activeIndex/Arrow/Enter/Escape + `keepFlyoutOnScreen` machinery. Plus `ProviderSelector.svelte`, `identify/IdentifyProviderSelect.svelte` are 4th/5th copies. Extract one `FlyoutListbox` core (see NEW below) that all three forms pickers consume. |
| `SearchInput` (icon + clear) | **NEW** | `packages/ui-svelte` → `SearchInput.svelte` | The single most-duplicated control: `EntityGridToolbar:237-256`, `FileTreePane:360-365`, `FileDetailPane`, `AddToCollectionMenu:104-112`, `AutoIdentifySection:234-246`, `ProviderSelector:87-93`, `PdfReader:742-781`, `CommandPalette:213`, `DestinationPicker:49`, `search/+page.svelte`, plus 4 plugin tabs. ~13 raw `<input type=search>` collapse here. |
| `DateField` | **CONSOLIDATE** | `apps/web-svelte/src/lib/components/forms/DateField.svelte` | Raw `<input type=date>` in `EntityGridFilterDrawer`, `search/+page.svelte:331`, `ConditionBuilder:308-397` fold in. |
| `NumberStepper` | **CONSOLIDATE** | promote to `packages/ui-svelte` | `settings/NumberStepper.svelte` duplicates the stepper inside `SettingsControl.svelte:94-117`. One winner. |
| `RangeField` / `Slider` | **CONSOLIDATE** | `packages/ui-svelte` → `RangeField.svelte` | Range `<input>` re-styled **6×**: `SubtitleSettingsPanel:100-160`, `VideoSettingsMenu:228`, `VideoPlayer:1751`, `settings/QualitySlider.svelte`, `SubtitlesSection:144-196`, `AudioVidStackPlayer:624-632`. |
| `Checkbox` | **EXISTS** | `packages/ui-svelte/src/primitives/Checkbox.svelte` | Raw checkboxes in `ComicReader:372`, `EntityThumbnail:629-639` (the overlay one is justified), `IdentifyDashboard` fold in. |
| `Toggle` | **EXISTS** | `packages/ui-svelte/src/primitives/Toggle.svelte` | — |
| `ToggleChip` | **EXISTS** | `apps/web-svelte/src/lib/components/forms/ToggleChip.svelte` | The canonical toggle-chip. `ConditionBuilder:426-446`, `AutoIdentifySection:198-210`, `WatchedLibrariesSection:418-514`, `EntityGridFilterDrawer` (~10 hand-rolled chips) must consume it. |
| `Chip` / `Badge` | **CONSOLIDATE** | `packages/ui-svelte/src/primitives/Badge.svelte` | **15 chip duplicates.** Define tone variants (`default/accent/info/success/warning/error/danger`) once. Fold `NsfwChip`, `NsfwShowModeChip`, `EntityFeed.feed-chip`, `EntityThumbnail:642-665/693-704`, `VideoStatusBar` spec/method chips, `EntityGridToolbar:473-478` filter chips, `TagsSection`, `EntityTagChips`. `video-tag-colors.ts` (dead, no importers) feeds the variant map or is deleted. |
| `StarRating` | **CONSOLIDATE** | `apps/web-svelte/src/lib/components/StarRatingPicker.svelte` (+ `InlineRating` for optimistic) | `UniversalLightbox:384-391` and `search/+page.svelte:299` re-implement; compose `StarRatingPicker`. |
| `TagSelect` | **EXISTS** | `apps/web-svelte/src/lib/components/forms/TagSelect.svelte` | `ChipInput.svelte` is a parallel bespoke tag input → retire into this. |
| `ListEditor` / `KeyValueEditor` / `MarkdownEditor` | **EXISTS** | `forms/*` | Already canonical; keep. |

### Layout Shells

| Block | Status | Canonical home | Notes |
|---|---|---|---|
| `PageHeader` (icon + title + gradient underline + optional action) | **NEW** | `packages/ui-svelte` → `PageHeader.svelte` | Currently inlined in `EntityIndexPage:276-300` (with `.page-head` CSS) and re-implemented per index/detail page. Pinned by `EntityIndexPage.header.test.ts`. Extract so detail pages stop re-stacking divs. |
| `EntityIndexPage` | **EXISTS** | `apps/web-svelte/src/lib/components/entities/EntityIndexPage.svelte` | Config-driven, already reused by 8 index routes (`videos`, `movies`, `comics`, `ebooks`, `books`, `galleries`, `series`, `artists`). The proven model to copy. |
| `EntityDetailPage` (config-driven detail scaffold) | **NEW** (split from monolith) | `apps/web-svelte/src/lib/components/entities/` | `EntityDetail.svelte` is a **2723-line monolith** doing hero, asset upload/dropzone, rating, action badges, tabs, ~16 metadata sections, full inline edit. Split into `EntityDetailHero`, `EntityDetailSections`, `EntityDetailEditForm` composed under a thin `EntityDetailPage` shell. This is the detail-side twin of `EntityIndexPage`. |
| `EntityContentSection` (icon + title + count badge + slot grid) | **NEW** | `apps/web-svelte/src/lib/components/entities/` | The `.content-section`/`.content-heading`/`.content-count` CSS triple is **copy-pasted verbatim** across `series/[id]` (3×, confirmed lines 281/299/316), `series/.../seasons/[seasonId]`, `galleries/[id]:312`, `books/[id]` + chapter + volume routes, `audio/[id]:362`. One block kills all of them. |
| `HierarchyShell` / `HierarchySection` / `HierarchyBreadcrumbs` | **EXISTS** | `apps/web-svelte/src/lib/components/shared/` | `HierarchyShell` is a thin 23-line `space-y` wrapper — keep, but it should host `PageHeader` once that exists. |
| `MediaShelf` / `EntityRail` (titled horizontal snap carousel + "View all") | **NEW** | `apps/web-svelte/src/lib/components/entities/` | Only `routes/+page.svelte:137-163` has it (`{#snippet shelf}`); every related-content row will want it. Also unifies the two thumbnail rails: `EntityDetailReferenceRail` + `EntityCastAndCrewSection` (`credit-scroller`). |
| `EditFormShell` + `FormActions` | **EXISTS** | `forms/EditFormShell.svelte`, `forms/FormActions.svelte` | Editor pages (`CollectionEditor:289-516`, `collections/[id]/edit`) re-roll the cancel-link/gradient-save/error-banner header; route them through these. |
| `master-detail` split scaffold | **EXISTS** (pattern) | `routes/files/+page.svelte:441` | Generalize only if a 2nd consumer appears. |

### Data Display

| Block | Status | Canonical home | Notes |
|---|---|---|---|
| `EntityGrid` | **EXISTS** | `entities/EntityGrid.svelte` | Grid/list/feed + toolbar + pagination. The data-wall workhorse. |
| `EntityThumbnail` / `MediaCard` | **EXISTS** | `entities/thumbnails/EntityThumbnail.svelte`, `packages/ui-svelte/composed/MediaCard.svelte` | Canonical card. `SearchResultCard` (3 bespoke variants), `IdentifyChildrenGrid` selectable tile, `ImageFeedItem` should converge here. |
| `InfoRow` / `DefinitionList` | **CONSOLIDATE** | `apps/web-svelte/src/lib/components/InfoRow.svelte` | `VideoFileInfo` key-value rows, `MetadataCard` rows fold in as a `DefinitionList`. |
| `Meter` / `ProgressMeter` | **EXISTS** | `packages/ui-svelte/composed/Meter.svelte` | Hand-rolled `.meter-track/.meter-fill` in `jobs/ActiveJobCard`, `MediaProgressPanel:104-118` must use `Meter`. |
| `StatTile` / `StatusBadge` / `EmptyState` | **NEW** | `packages/ui-svelte` | `jobs/OverviewStat` (stat tile), `jobs/+page.svelte:303` + `CompletedJobRow` (status badge), `jobs/EmptyPanel` (empty state) generalize to ui-svelte. |
| `TabStrip` / `SegmentedControl` | **NEW** | `packages/ui-svelte` | `EntityGridTabs:15-38` (count tabs), `EntityGridToolbar:319-354` (view toggle), `settings/+page.svelte:463`, `ConditionBuilder:206-237`, `CollectionEditor:368-431`, `PluginPageShell:47`, `identify/+page.svelte:78` all re-roll segmented/tab strips. Two primitives (`TabStrip` for nav, `SegmentedControl` for value selection) cover them. |

### Overlays

| Block | Status | Canonical home | Notes |
|---|---|---|---|
| `Dialog` / `Modal` | **CONSOLIDATE → NEW base** | `packages/ui-svelte` → `Dialog.svelte` | **~10 hand-rolled backdrops.** Native-`<dialog>` scaffold duplicated verbatim in `nav/MoveToSectionDialog` ↔ `nav/RenameSectionDialog`; fixed-inset variants in `ConfirmDeleteDialog`, `DestinationPicker`, `ImagePickerModal`, `CommandPalette`, `LibraryRootPicker`, `SubtitleSettingsPanel:34-48`, `settings/+page.svelte:854`, `EntityDetail` discard-edits modal. Build one `Dialog` (backdrop + Escape + focus-trap + scroll-lock); `ConfirmDialog` (EXISTS, `entities/ConfirmDialog.svelte`) and `NameInputDialog` (EXISTS) re-base onto it. |
| `Popover` / `Flyout` | **NEW** | `packages/ui-svelte` → `Popover.svelte` | Trigger + full-screen close-backdrop + `keepFlyoutOnScreen` panel re-coded in `EntityGridPagination`, `EntityGridToolbar:258-417`, `EntityGridPresetDropdown`, `AddToCollectionMenu` (comment literally says copy-pasted), `PlaybackQueueFlyout`, `FileTreePane:50-134`, `CanvasHeader`. One `Popover` wraps the existing `keepFlyoutOnScreen` action (`$lib/actions/keep-flyout-on-screen.ts`). |
| `BottomSheet` | **NEW** | `packages/ui-svelte` | `MobileMoreSheet` has the full gesture/animation rig; extract chrome from nav content so mobile filters/pickers reuse it. |
| `Drawer` / `SlideOver` | **NEW** | `packages/ui-svelte` | `EntityGridFilterDrawer`, `PdfReader:784-818` TOC slide-over. |
| `Lightbox` | **EXISTS** | `apps/web-svelte/src/lib/components/UniversalLightbox.svelte` | Canonical; `ImagePickerModal` nav can lean on `universal-lightbox-media.ts` helpers. |
| `ActionMenu` (kebab) | **NEW** | `packages/ui-svelte` | `TrackListRow:239-300` `role=menu` flyout; composes `Popover`. |

### Action Affordances

| Block | Status | Canonical home | Notes |
|---|---|---|---|
| `Button` | **EXISTS** | `packages/ui-svelte/primitives/Button.svelte` | Raw `<button>`s in `UploadDropZone:133`, `DiagnosticsSection:60-100`, `collections/[id]`, `identify/[entityId]:248` must use it. |
| `IconButton` / `RowActions` | **NEW** | `packages/ui-svelte` | Pencil/Trash2 hover-reveal clusters in `VideoMarkerEditor:149`, `VideoTranscriptPanel:382`, `jobs/RunCatalogRow`, `design-language:505`. One `IconButton` + a `RowActions` group. |
| `EntityActionButton` / action-badge | **EXISTS** | `entities/IdentifyButton.svelte` + global `.entity-action-button` | `MediaProgressPanel:122-134` mirrors it by hand → reference the shared class/component. |
| `BulkActionBar` | **EXISTS** | `apps/web-svelte/src/lib/components/BulkActionBar.svelte` | Already cleanly parameterized; catalog as canonical selection toolbar. |

---

## 2. Composition Kit — assembling a brand-new entity page

Goal: a new entity (say **"Podcasts"**) ships **index + detail + edit** with effectively zero bespoke markup, mirroring how `videos`/`movies`/`comics` already reuse `EntityIndexPage`. The pattern follows the canonical-code discipline: a route file is *config*, not *markup*.

### Index route — `routes/podcasts/+page.svelte`
Already achievable today. Pure config into `EntityIndexPage`:

```svelte
<script lang="ts">
  import EntityIndexPage from "$lib/components/entities/EntityIndexPage.svelte";
  import { ENTITY_KIND } from "$lib/entities/entity-codes"; // canonical code, not "podcast" literal
  import { Podcast } from "@lucide/svelte";
</script>

<EntityIndexPage
  kind={ENTITY_KIND.PODCAST}
  prefsKey="podcasts"
  icon={Podcast}
  title="Podcasts"
  emptyTitle="No podcasts yet"
  emptyMessage="Scan a library to populate podcasts."
  enableFeedView
/>
```
`EntityIndexPage` internally renders `PageHeader` (NEW) + `EntityGrid` + `BulkActionBar` + `ConfirmDialog`/`NameInputDialog`. No CSS, no toolbar, no pagination code in the route.

### Detail route — `routes/podcasts/[id]/+page.svelte`
After the `EntityDetail` split, the route is a `LoadState` machine feeding a **config-driven** `EntityDetailPage`:

```svelte
<EntityDetailPage
  card={podcast}
  tabs={[
    { id: "overview", label: "Overview", sections: ["studio", "stats", "dates"] },
    { id: "episodes", label: "Episodes", count: episodeCards.length, sections: ["episodes"] },
  ]}
  actions={[favoriteAction, organizedAction, identifyAction]}  {/* EntityDetailActionButton[] */}
>
  {#snippet sections(id)}
    {#if id === "episodes"}
      <EntityContentSection icon={ListMusic} title="Episodes" count={episodeCards.length}>
        <EntityGrid cards={episodeCards} entityKind={ENTITY_KIND.PODCAST_EPISODE} prefsKey="podcast-episodes" />
      </EntityContentSection>
    {/if}
  {/snippet}

  {#snippet related()}
    <MediaShelf title="From this studio" cards={studioCards} viewAllHref={studioHref} />
  {/snippet}
</EntityDetailPage>
```
Built-in metadata sections (studio/credits/stats/dates/technical/classification/sources/fingerprints) render from the existing `EntityDetailSection` registry — the route only supplies *child* sections via `EntityContentSection` + `EntityGrid`, the exact triple that is currently copy-pasted across 6 routes.

### Edit form — driven by the same draft model
Edit mode lives **inside** `EntityDetailPage` (toggled), reusing `EntityDetailEditForm` (split out of the monolith) which already composes `TextField`, `MarkdownEditor`, `EntityPicker`, `TagSelect`, `ListEditor`, `KeyValueEditor`, `ToggleChip`, `FormActions`, and the existing `entity-detail-edit.ts` draft/validate/serialize helpers (`draftFromCard`, `validateDraft`, `buildMetadataUpdate`). A standalone editor (e.g. collections) instead wraps the same fields in `EditFormShell` + `FormActions`.

### Net result
A new entity page = **one config object per surface** (index props, tab/section config, edit draft schema). Every visual atom — header, grid, chips, dialogs, rails, sections, form controls — resolves to a shared block. The only entity-specific code is the `{#snippet sections}` branch and the API load calls.

---

## 3. Consolidation / Migration Map (ordered by leverage)

Each wave is independently shippable and reduces the raw-control surface. Order maximizes drift-elimination per change.

**Wave 1 — The three competing flyout pickers (highest leverage).**
Extract `FlyoutListbox` core (search-input + `role=listbox` + activeIndex/Arrow/Enter/Escape + add-row + `keepFlyoutOnScreen`). Re-base:
`forms/SearchSelect.svelte`, `forms/TagSelect.svelte`, `forms/EntityPicker.svelte` → consume it.
Then retire the 4th/5th copies: `ProviderSelector.svelte`, `identify/IdentifyProviderSelect.svelte`, `ChipInput.svelte`.
*One interaction model for every dropdown/combobox in the app.*

**Wave 2 — Text inputs + the 52 raw-control files.**
This is the adoption gap (2 forms-barrel importers vs 96 ui-svelte importers). Sweep raw `<input>`/`<select>`/`<button>` → `TextField`/`Select`/`Button`:
- Text: `TimeMarkerForm:134-140`, `Sidebar:245-253`, `TrackListRow:150-160`, `VideoTranscriptPanel:317`, `EntityGridPresetDropdown:138-148`, `RenameSectionDialog`, `WatchedLibrariesSection:241-250`, `IdentifyReviewChoice:239`, `StashBoxEndpointsTab:185`, `settings/+page.svelte:593`, `ConditionBuilder:308-397`.
- Select: `ConditionBuilder:270-398`.
- Build `SearchInput` (NEW), migrate the ~13 search boxes listed in §1.
*Largest file-count reduction; pure mechanical adoption of existing primitives.*

**Wave 3 — Chips & badges (15 duplicates).**
Define `Badge`/`Chip` tone variants once; migrate `NsfwChip`, `NsfwShowModeChip`, `TagsSection`, `EntityTagChips`, `EntityFeed.feed-chip`, `EntityGridToolbar:473-478`, `EntityThumbnail:642-704`, `VideoStatusBar`, `EntityPicker`/`TagSelect` selected-chips. Delete or rewire `video-tag-colors.ts`. Collapse `NsfwBlur`/`NsfwText`/`NsfwTagLabel` → one `NsfwGate` with an `as` prop.

**Wave 4 — Overlays (`Dialog` + `Popover`).**
Build `Dialog` base; re-base `ConfirmDialog`, `NameInputDialog`, then migrate `ConfirmDeleteDialog`, `MoveToSectionDialog`, `RenameSectionDialog`, `LibraryRootPicker`, `DestinationPicker`, `ImagePickerModal`, `CommandPalette`, `SubtitleSettingsPanel`, `settings/+page.svelte:854`, and `EntityDetail`'s discard modal.
Build `Popover` over `keepFlyoutOnScreen`; migrate `EntityGridPagination`, `EntityGridToolbar:258-417`, `EntityGridPresetDropdown`, `AddToCollectionMenu`, `PlaybackQueueFlyout`, `FileTreePane:50-134`, `CanvasHeader`.

**Wave 5 — The movies/videos detail fork + content-section.**
Split `EntityDetail.svelte` (2723 lines) → `EntityDetailPage` + `EntityDetailHero` + `EntityDetailSections` + `EntityDetailEditForm`. Extract `EntityContentSection` and migrate the copy-pasted triple in `series/[id]`, `series/.../seasons/[seasonId]`, `galleries/[id]`, `books/[id]` (+ chapters/volumes), `audio/[id]`. Resolve the movies vs videos detail fork (`movies/[id]:735` vs `videos/[id]:790-843`) onto the same `EntityDetailPage`. Extract `ResizableSplit` (shared by `movies/[id]:698` and `videos/[id]:753-768` transcript docks) along the way.

**Wave 6 — Rails, ranges, meters, jobs primitives.**
`MediaShelf`/`EntityRail` (from `+page.svelte:137-163`, unifying `EntityDetailReferenceRail` + `EntityCastAndCrewSection`). `RangeField` (6 callers). `Meter` adoption (`ActiveJobCard`, `MediaProgressPanel`). New `StatTile`/`StatusBadge`/`EmptyState`/`TabStrip`/`SegmentedControl`/`IconButton` with their job/settings/grid callers.

**Wave 7 — Reader + player tail (lowest leverage, most specialized).**
Hoist shared reader nav/mode/gesture out of `BookFileReader` ↔ `ComicReader` into reader primitives (both already use `ReaderShell`). Extract `TimeField`, `CueList` (dedupe `VideoTranscriptPanel:248` vs `496-517`), `Slider`/scrub strip shared between `MediaCard` and the player, `FilmStrip`/`thumbnail-strip`.

---

## 4. Where blocks live — `packages/ui-svelte` vs `apps/web-svelte/.../forms`

**The rule (one sentence):** if a block knows nothing about Prismedia domain types, codes, API, stores, or routing, it lives in **`packages/ui-svelte`**; if it binds to domain shapes (`EntityCard`, capabilities, `ENTITY_KIND`, `$lib/api/*`, `$lib/nsfw`, `$app/navigation`), it lives in **`apps/web-svelte/src/lib/components`** (forms primitives in `.../forms`, entity scaffolds in `.../entities`).

Concretely:

- **`packages/ui-svelte/primitives` & `composed`** (presentational, dependency-free, themeable via tokens): `TextInput`, `Select`, `Checkbox`, `Toggle`, `Button`, `Badge`/`Chip`, `Meter`, `MediaCard`, plus the NEW `SearchInput`, `RangeField`, `NumberStepper`, `Dialog`, `Popover`, `BottomSheet`, `Drawer`, `ActionMenu`, `IconButton`, `TabStrip`, `SegmentedControl`, `StatTile`, `StatusBadge`, `EmptyState`, `PageHeader`. These have **no `$lib`, `$app`, or `$lib/api` imports** — that constraint is the membership test (and is mechanically lintable).

- **`apps/web-svelte/src/lib/components/forms`** (domain-aware field wrappers): `FormField`, `TextField`, `TextAreaField`, `DateField`, `SearchSelect`, `TagSelect`, `EntityPicker`, `ListEditor`, `KeyValueEditor`, `MarkdownEditor`, `ToggleChip`, `FormActions`, `EditFormShell`. These wrap ui-svelte primitives and bind to validation/search-source/draft helpers (`entity-detail-edit.ts`, `entity-detail-search.ts`). The NEW `FlyoutListbox` core can live here too since all three consumers are domain pickers.

- **`apps/web-svelte/src/lib/components/entities` & `shared`** (domain scaffolds): `EntityIndexPage`, the NEW `EntityDetailPage`/`EntityDetailHero`/`EntityDetailSections`/`EntityDetailEditForm`, `EntityContentSection`, `MediaShelf`/`EntityRail`, `EntityGrid`, `EntityThumbnail`, `BulkActionBar`, `HierarchyShell`. They consume both ui-svelte primitives and forms wrappers and know about `EntityCard`/`ENTITY_KIND`.

**Why this split mirrors the backend:** ui-svelte is the design-system "kernel" (like `EnumCodec`/`CodecRegistry` — pure, reusable, no domain knowledge); the app `forms`/`entities` layer is the domain projection (like `EntityKindRegistry` binding `[Code]` to entity meta). Keeping presentational primitives domain-free is what lets the index/detail scaffolds stay thin and config-driven — and keeps the `@prismedia/ui-svelte` package independently testable, the same boundary discipline `CLAUDE.md` already mandates for app vs packages.