<script lang="ts">
  import {
    ArrowUpDown,
    Check,
    ChevronDown,
    ChevronsDownUp,
    ChevronsUpDown,
    Grid2x2,
    Grid3x3,
    Image,
    LayoutGrid,
    List,
    Rows3,
    RotateCcw,
    Search,
    Shuffle,
    SlidersHorizontal,
    X,
  } from "@lucide/svelte";
  import { onMount } from "svelte";
  import { slide } from "svelte/transition";
  import { cubicOut } from "svelte/easing";
  import { browser } from "$app/environment";
  import { cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import type { FilterPreset } from "$lib/filter-presets";
  import { entityGridFilterFromId } from "$lib/entities/entity-grid";
  import type {
    EntityGridFilterOption,
    EntityGridBulkAction,
    EntityGridSort,
    EntityGridSortDir,
    EntityGridViewMode,
  } from "$lib/entities/entity-grid";
  import type { CollectionEntityType } from "$lib/collections/models";
  import BulkSelectionBar from "./BulkSelectionBar.svelte";
  import EntityGridPresetDropdown from "./EntityGridPresetDropdown.svelte";

  interface Props {
    activeFilterIds: string[];
    activePresetId?: string | null;
    allSelectedNsfw: boolean;
    /** True when every selected card is a wanted placeholder; enables the Remove wanted bulk action. */
    allSelectedWanted?: boolean;
    /** Removes the selected wanted placeholders (delete + discovery blacklist). */
    onRemoveWanted?: () => void;
    /** Persisted collapse state for the secondary toolbar rows; seeds the initial view. */
    barsCollapsed?: boolean;
    bulkActions: EntityGridBulkAction[];
    /** Collection-eligible members of the current selection, used by the Add to Collection menu. */
    collectionItems: { entityType: CollectionEntityType; entityId: string }[];
    canClearFiltersAndSort: boolean;
    /** When true, exposes the vertical feed view mode toggle. */
    enableFeedView?: boolean;
    drawerOpen: boolean;
    /** Kind code of the grid, used to offer kind-specific sorts (e.g. references for taxonomy). */
    entityKind?: string;
    filterOptions: EntityGridFilterOption[];
    maxScale: number;
    minScale: number;
    onActiveFilterIdsChange: (ids: string[]) => void;
    onApplyPreset: (preset: FilterPreset) => void;
    /** Fired when the user manually collapses/expands the secondary rows, so the state can persist. */
    onBarsCollapsedChange?: (collapsed: boolean) => void;
    onClearFiltersAndSort: () => void;
    onClearSelection: () => void;
    onDeletePreset: (id: string) => void;
    onDrawerOpenChange: (open: boolean) => void;
    onSelectAllVisible: () => void;
    onSelectionActiveChange: (active: boolean) => void;
    onOverwritePreset: (id: string) => void;
    onQueryChange: (query: string) => void;
    onMediaWallChange: (mediaWall: boolean) => void;
    onSavePreset: (name: string) => void;
    onScaleChange: (scale: number) => void;
    onSortByChange: (sortBy: EntityGridSort) => void;
    onSortDirChange: (sortDir: EntityGridSortDir) => void;
    onToggleNsfwFlag: (markNsfw: boolean) => void;
    onReshuffle: () => void;
    onViewModeChange: (viewMode: EntityGridViewMode) => void;
    presets: FilterPreset[];
    mediaWall: boolean;
    query: string;
    scale: number;
    selectable: boolean;
    /** Whether the selection bar offers the Mark NSFW action (off for non-library grids). */
    showNsfwAction?: boolean;
    selectedCount: number;
    selectedIds: string[];
    selectionActive: boolean;
    sortBy: EntityGridSort;
    sortDir: EntityGridSortDir;
    viewMode: EntityGridViewMode;
  }

  let {
    activeFilterIds,
    activePresetId = null,
    allSelectedNsfw,
    allSelectedWanted = false,
    onRemoveWanted,
    barsCollapsed: initialBarsCollapsed = false,
    bulkActions,
    collectionItems,
    canClearFiltersAndSort,
    enableFeedView = false,
    drawerOpen,
    entityKind,
    filterOptions,
    maxScale,
    minScale,
    onActiveFilterIdsChange,
    onApplyPreset,
    onBarsCollapsedChange,
    onClearFiltersAndSort,
    onClearSelection,
    onDeletePreset,
    onDrawerOpenChange,
    onSelectAllVisible,
    onSelectionActiveChange,
    onOverwritePreset,
    onQueryChange,
    onMediaWallChange,
    onSavePreset,
    onScaleChange,
    onSortByChange,
    onSortDirChange,
    onToggleNsfwFlag,
    onReshuffle,
    onViewModeChange,
    presets,
    mediaWall,
    query,
    scale,
    selectable,
    showNsfwAction = true,
    selectedCount,
    selectedIds,
    selectionActive,
    sortBy,
    sortDir,
    viewMode,
  }: Props = $props();

  const SORT_LABELS: Record<EntityGridSort, string> = {
    title: "Title",
    added: "Date added",
    rating: "Rating",
    random: "Random",
    kind: "Kind",
    position: "Position",
    references: "References",
  };

  // Reference-count sort only applies to taxonomy kinds (tags/people/studios), which are the
  // targets of relationship links; it is the default sort for those grids.
  const TAXONOMY_KINDS = new Set(["tag", "person", "studio"]);
  const SORT_OPTIONS = $derived<{ value: EntityGridSort; label: string }[]>([
    { value: "title", label: "Title" },
    { value: "added", label: "Date added" },
    ...(entityKind != null && TAXONOMY_KINDS.has(entityKind)
      ? [{ value: "references" as const, label: "References" }]
      : []),
    { value: "rating", label: "Rating" },
    { value: "random", label: "Random" },
    { value: "kind", label: "Kind" },
    { value: "position", label: "Position" },
  ]);

  let sortOpen = $state(false);
  let thumbSizeOpen = $state(false);

  const activeFilters = $derived(
    activeFilterIds
      .map((id) => entityGridFilterFromId(id, filterOptions))
      .filter((option): option is EntityGridFilterOption => Boolean(option)),
  );

  // The active-filter chip row and the selection/bulk row are the two secondary
  // toolbar rows that can be collapsed to keep the bar compact (especially on
  // mobile). The toggle only appears when at least one of them is present.
  const hasCollapsibleRows = $derived(
    selectable || activeFilters.length > 0 || canClearFiltersAndSort,
  );

  // Seed from the persisted preference. A saved collapsed state is treated as a
  // manual choice, so it starts pinned and scrolling won't immediately undo it.
  // svelte-ignore state_referenced_locally
  let barsCollapsed = $state(initialBarsCollapsed);
  // Once the user collapses/expands by hand, scrolling stops driving the state.
  // svelte-ignore state_referenced_locally
  let collapsePinned = $state(initialBarsCollapsed);

  function toggleBars() {
    barsCollapsed = !barsCollapsed;
    collapsePinned = true;
    // Only manual toggles persist; scroll-driven collapse stays ephemeral.
    onBarsCollapsedChange?.(barsCollapsed);
  }

  // Collapse the secondary rows once the user scrolls down into the content, and
  // then leave them alone. Scrolling up deliberately does NOT bring them back —
  // the earlier auto re-expand fought the user and stuttered the bars in and out
  // as scroll direction wavered. The rows stay hidden until the user taps the
  // (accented) toggle, and any manual toggle pins the state so scrolling stops
  // touching it entirely.
  onMount(() => {
    if (!browser) return;
    let lastY = window.scrollY;

    function scrollTopOf(target: EventTarget | null): number {
      if (target instanceof HTMLElement) return target.scrollTop;
      return window.scrollY;
    }

    function onScroll(event: Event) {
      if (collapsePinned || barsCollapsed) return;
      const y = scrollTopOf(event.target);
      const delta = y - lastY;
      lastY = y;
      if (delta > 8 && y > 48) barsCollapsed = true;
    }

    window.addEventListener("scroll", onScroll, { capture: true, passive: true });
    return () => window.removeEventListener("scroll", onScroll, { capture: true });
  });

  function removeFilter(id: string) {
    onActiveFilterIdsChange(activeFilterIds.filter((filterId) => filterId !== id));
  }

  function parseScale(event: Event) {
    onScaleChange(Number((event.currentTarget as HTMLInputElement).value));
  }
</script>

<div class="toolbar-shell">
  <div class="toolbar-stack">
    <div class="toolbar-hero">
    <div class="search-row">
      <label class="search-box">
        <Search class="search-icon" aria-hidden="true" />
        <input
          type="search"
          placeholder="Search the library…"
          value={query}
          oninput={(event) => onQueryChange((event.currentTarget as HTMLInputElement).value)}
        />
        {#if query}
          <button
            type="button"
            class="search-clear"
            title="Clear search"
            aria-label="Clear search"
            onclick={() => onQueryChange("")}
          >
            <X class="h-3 w-3" />
          </button>
        {/if}
      </label>

      <div class="search-sort">
        <div class="relative">
          <button
            type="button"
            class="ctrl-btn ctrl-sort"
            onclick={() => (sortOpen = !sortOpen)}
          >
            <ArrowUpDown class="h-3.5 w-3.5" />
            <span class="ctrl-label">{SORT_LABELS[sortBy]}</span>
            <ChevronDown class="h-3 w-3 text-text-disabled" />
          </button>

          {#if sortOpen}
            <button
              type="button"
              class="fixed inset-0 z-40"
              aria-label="Close sort menu"
              onclick={() => (sortOpen = false)}
            ></button>
            <div class="sort-menu sort-menu-end" use:keepFlyoutOnScreen>
              {#each SORT_OPTIONS as opt (opt.value)}
                <button
                  type="button"
                  class={cn("sort-menu-item", sortBy === opt.value && "is-active")}
                  onclick={() => {
                    onSortByChange(opt.value);
                    sortOpen = false;
                  }}
                >
                  <Check class={cn("h-3 w-3", sortBy === opt.value ? "opacity-100" : "opacity-0")} />
                  {opt.label}
                </button>
              {/each}
            </div>
          {/if}
        </div>

        {#if sortBy === "random"}
          <button
            type="button"
            class="ctrl-btn ctrl-icon"
            title="Reshuffle"
            aria-label="Reshuffle the random order"
            onclick={() => onReshuffle()}
          >
            <Shuffle class="h-3.5 w-3.5" />
          </button>
        {:else}
          <button
            type="button"
            class="ctrl-btn ctrl-icon"
            title={sortDir === "asc" ? "Ascending — click to reverse" : "Descending — click to reverse"}
            aria-label={`Sort direction: ${sortDir}`}
            onclick={() => onSortDirChange(sortDir === "asc" ? "desc" : "asc")}
          >
            <ChevronDown class={cn("h-3.5 w-3.5 dir-arrow", sortDir === "asc" && "is-up")} />
          </button>
        {/if}
      </div>
    </div>

    <div class="controls-row">
      <div class="control-cluster">
        <div class="view-toggle" aria-label="View mode">
          <button
            type="button"
            class:is-active={viewMode === "grid"}
            title="Grid view"
            aria-label="Grid view"
            aria-pressed={viewMode === "grid"}
            onclick={() => onViewModeChange("grid")}
          >
            <LayoutGrid class="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            class:is-active={viewMode === "list"}
            title="List view"
            aria-label="List view"
            aria-pressed={viewMode === "list"}
            onclick={() => onViewModeChange("list")}
          >
            <List class="h-3.5 w-3.5" />
          </button>
          {#if enableFeedView}
            <button
              type="button"
              class:is-active={viewMode === "feed"}
              title="Feed view"
              aria-label="Feed view"
              aria-pressed={viewMode === "feed"}
              onclick={() => onViewModeChange("feed")}
            >
              <Rows3 class="h-3.5 w-3.5" />
            </button>
          {/if}
        </div>

        {#if viewMode !== "list"}
          <button
            type="button"
            class={cn("ctrl-btn ctrl-icon", mediaWall && "is-active")}
            title="Media wall"
            aria-label="Media wall"
            aria-pressed={mediaWall}
            onclick={() => onMediaWallChange(!mediaWall)}
          >
            <Image class="h-3.5 w-3.5" />
          </button>

          <label class="thumb-size-inline" title="Drag to change thumbnail size">
          <Grid2x2 class="thumb-size-icon thumb-size-icon-min" aria-hidden="true" />
          <span class="sr-only">Thumbnail columns</span>
          <input
            type="range"
            aria-label="Thumbnail columns"
            min={minScale}
            max={maxScale}
            step="1"
            value={scale}
            oninput={parseScale}
          />
          <Grid3x3 class="thumb-size-icon thumb-size-icon-max" aria-hidden="true" />
        </label>

        <div class="thumb-size-compact relative">
          <button
            type="button"
            class={cn("ctrl-btn ctrl-icon", thumbSizeOpen && "is-active")}
            title="Thumbnail size"
            aria-label="Thumbnail size"
            aria-expanded={thumbSizeOpen}
            onclick={() => (thumbSizeOpen = !thumbSizeOpen)}
          >
            <LayoutGrid class="h-3.5 w-3.5" />
          </button>

          {#if thumbSizeOpen}
            <button
              type="button"
              class="fixed inset-0 z-40"
              aria-label="Close thumbnail size menu"
              onclick={() => (thumbSizeOpen = false)}
            ></button>
            <div class="thumb-size-popover" use:keepFlyoutOnScreen>
              <Grid2x2 class="thumb-size-icon thumb-size-icon-min" aria-hidden="true" />
              <span class="sr-only">Thumbnail columns</span>
              <input
                type="range"
                aria-label="Thumbnail columns"
                min={minScale}
                max={maxScale}
                step="1"
                value={scale}
                oninput={parseScale}
              />
              <Grid3x3 class="thumb-size-icon thumb-size-icon-max" aria-hidden="true" />
            </div>
          {/if}
        </div>
        {/if}
      </div>

      <div class="control-cluster control-cluster-trailing">
        <button
          type="button"
          class={cn("ctrl-btn ctrl-filters", drawerOpen && "is-active")}
          aria-expanded={drawerOpen}
          onclick={() => onDrawerOpenChange(!drawerOpen)}
        >
          <SlidersHorizontal class="h-3.5 w-3.5" />
          <span class="ctrl-label">Filters</span>
          {#if activeFilterIds.length > 0}
            <span class="filter-count">{activeFilterIds.length}</span>
          {/if}
        </button>

        <EntityGridPresetDropdown
          {activePresetId}
          {presets}
          {onApplyPreset}
          {onSavePreset}
          {onOverwritePreset}
          {onDeletePreset}
        />

        {#if hasCollapsibleRows}
          <button
            type="button"
            class="ctrl-btn ctrl-icon collapse-toggle"
            class:is-active={barsCollapsed}
            title={barsCollapsed ? "Show filter and selection rows" : "Hide filter and selection rows"}
            aria-label={barsCollapsed ? "Show filter and selection rows" : "Hide filter and selection rows"}
            aria-expanded={!barsCollapsed}
            onclick={toggleBars}
          >
            {#if barsCollapsed}
              <ChevronsUpDown class="h-3.5 w-3.5" />
            {:else}
              <ChevronsDownUp class="h-3.5 w-3.5" />
            {/if}
          </button>
        {/if}
      </div>
    </div>
    </div>

    {#if !barsCollapsed && (activeFilters.length > 0 || canClearFiltersAndSort)}
    <div class="filter-row toolbar-bar" transition:slide={{ duration: 200, easing: cubicOut }}>
      <div class="filter-scroll" aria-live="polite">
        {#if activeFilters.length > 0}
          <span class="filter-chip-label" aria-hidden="true">
            <SlidersHorizontal class="h-3 w-3 shrink-0" />
            ACTIVE
          </span>
          {#each activeFilters as option (option.id)}
            <button type="button" class="filter-chip" onclick={() => removeFilter(option.id)}>
              <span>{option.label}</span>
              <X class="h-3 w-3" />
            </button>
          {/each}
        {/if}
      </div>

      {#if canClearFiltersAndSort}
        <button
          type="button"
          title="Clear filters, sort, search, and saved preferences"
          class="ctrl-btn ctrl-clear filter-reset"
          onclick={onClearFiltersAndSort}
        >
          <RotateCcw class="h-3.5 w-3.5 shrink-0" />
          <span class="ctrl-label">Clear</span>
        </button>
      {/if}
    </div>
    {/if}

    {#if selectable && !barsCollapsed}
      <BulkSelectionBar
        {allSelectedNsfw}
        {allSelectedWanted}
        {onRemoveWanted}
        {bulkActions}
        {collectionItems}
        {onClearSelection}
        {onSelectAllVisible}
        {onSelectionActiveChange}
        {onToggleNsfwFlag}
        {showNsfwAction}
        {selectedCount}
        {selectedIds}
        {selectionActive}
        tuckedAfterPrevious={activeFilters.length > 0 || canClearFiltersAndSort}
      />
    {/if}
  </div>
</div>

<style>
  /*
   * The toolbar pins to the top of the layout's scrolling container so the
   * search box, sort/filter controls, and the active filter chip row stay
   * reachable as soon as the page scrolls past their natural position —
   * mirroring how the pagination strip locks to the bottom of the same
   * container.
   *
   * The shell carries an opaque base color (`--color-bg`) under the glass
   * panel so cards scrolling behind the docked toolbar can never bleed
   * through the blurred fill. `padding-top: 0.5rem` offsets the visible
   * glass panel from the top edge, mirroring the pagination shell below.
   *
   * The interior `.toolbar-root` panel uses the same glass recipe as the
   * pagination bar (semi-transparent surface tint + `backdrop-filter`) so
   * the two docked strips read as one continuous floating material above
   * the grid.
   *
   * Interactive controls share a single set of border / background / inset
   * tokens defined just below, so the search box, ctrl buttons, view
   * toggle, and thumbnail-size slider all read as the same family of
   * material chips instead of mismatched outlines.
   */
  .toolbar-shell {
    position: sticky;
    top: 0;
    z-index: 4;
    display: flex;
    flex-direction: column;
    padding-top: 0.5rem;
    background: transparent;
    pointer-events: none;

    --ctrl-border: var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    --ctrl-border-hover: var(--color-border-accent, rgba(242, 194, 106, 0.25));
    --ctrl-border-active: var(--color-border-accent, rgba(242, 194, 106, 0.25));
    --ctrl-bg: var(--color-surface-2, #101420);
    --ctrl-bg-hover: var(--color-surface-3, #151a28);
    --ctrl-bg-active: var(--color-surface-4, #1c2235);
    --ctrl-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    --ctrl-shadow-hover: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
    --ctrl-shadow-active: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .toolbar-shell::before {
    display: none;
  }

  /*
   * The toolbar mirrors the EntityDetail layering: a primary "hero" panel
   * (search + controls) with all corners rounded, then lower bars that tuck
   * up behind it with only their bottom corners rounded so each reads as
   * sliding out from underneath the section above. The hero keeps the highest
   * stacking order inside the shell so the lower bars disappear behind its
   * bottom edge.
   */
  .toolbar-stack {
    display: flex;
    flex-direction: column;
    min-width: 0;
    pointer-events: auto;

    --toolbar-detail-border: var(--color-border, #1c2235);
    --toolbar-detail-glass: rgb(12 15 21);
    --toolbar-detail-slideout-inset: 5px;
    --toolbar-bar-overlap: 0.5rem;
  }

  .toolbar-hero {
    position: relative;
    z-index: 3;
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
    border: 1px solid var(--toolbar-detail-border);
    border-radius: var(--radius-sm, 6px);
    background: var(--toolbar-detail-glass);
    box-shadow: 0 8px 40px rgba(0, 0, 0, 0.60);
    padding: 1rem 1.05rem;
  }

  .toolbar-hero::before {
    content: "";
    position: absolute;
    inset: 0 var(--radius-sm, 6px) auto var(--radius-sm, 6px);
    height: 1px;
    background:
      linear-gradient(
        to right,
        rgb(242 194 106 / 0.16) 0%,
        rgb(242 194 106 / 0.82) 14%,
        rgb(242 194 106 / 0.95) 50%,
        rgb(242 194 106 / 0.82) 86%,
        rgb(242 194 106 / 0.16) 100%
      );
    box-shadow: 0 0 12px rgb(242 194 106 / 0.35);
    pointer-events: none;
  }

  .toolbar-bar {
    position: relative;
    display: flex;
    align-items: center;
    min-width: 0;
    margin-inline: var(--toolbar-detail-slideout-inset);
    margin-top: calc(-1 * var(--toolbar-bar-overlap));
    border: 1px solid var(--toolbar-detail-border);
    border-top: 0;
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background: var(--toolbar-detail-glass);
  }

  .search-row {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    min-width: 0;
  }

  /* Compact sort controls tucked to the right of the search box. */
  .search-sort {
    display: flex;
    flex: 0 0 auto;
    align-items: center;
    gap: 0.35rem;
  }

  /* Anchor the dropdown to the right since the trigger sits near the edge. */
  .sort-menu-end {
    left: auto;
    right: 0;
  }

  .search-box {
    position: relative;
    display: flex;
    flex: 1 1 auto;
    align-items: center;
    gap: 0.55rem;
    min-width: 0;
    height: 2.1rem;
    background: var(--color-surface-1, #0c0f15);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    padding: 0 0.65rem;
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .search-box:focus-within {
    border-color: var(--color-border-accent, rgba(242, 194, 106, 0.25));
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30), 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .search-box :global(.search-icon) {
    width: 0.95rem;
    height: 0.95rem;
    color: var(--color-text-disabled);
    flex-shrink: 0;
  }

  .search-box:focus-within :global(.search-icon) {
    color: var(--color-text-accent);
  }

  .search-box input {
    min-width: 0;
    width: 100%;
    border: 0;
    background: transparent;
    color: var(--color-text-primary);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: 0.875rem;
    letter-spacing: 0;
    outline: 0;
  }

  .search-box input::placeholder {
    color: var(--color-text-disabled);
    font-style: italic;
  }

  /* Hide the native WebKit/Chromium search clear so it doesn't collide with our
     own brass-styled clear button. */
  .search-box input::-webkit-search-cancel-button,
  .search-box input::-webkit-search-decoration {
    appearance: none;
    -webkit-appearance: none;
    display: none;
  }

  .search-clear {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 1.25rem;
    height: 1.25rem;
    border: 1px solid transparent;
    background: transparent;
    color: var(--color-text-disabled);
    flex-shrink: 0;
    transition:
      color var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default);
  }

  .search-clear:hover {
    color: var(--color-text-accent);
    border-color: rgb(242 194 106 / 0.3);
  }

/*
   * Two clusters share one wrapping row. The trailing cluster uses
   * `margin-left: auto` so it always hugs the right edge — both when the
   * row has spare width and when the leading cluster wraps and pushes
   * trailing to its own line. Without this, `justify-content: space-between`
   * collapses to `flex-start` once items wrap, leaving the trailing cluster
   * stranded against the left edge with empty space on the right.
   */
  .controls-row {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.5rem 0.4rem;
    min-width: 0;
  }

  .control-cluster {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    min-width: 0;
    flex-wrap: wrap;
  }

  .control-cluster-trailing {
    margin-left: auto;
    justify-content: flex-end;
    flex-wrap: nowrap;
    flex-shrink: 0;
  }

  .ctrl-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    height: 2rem;
    min-height: 2rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    letter-spacing: 0.04em;
    padding: 0 0.6rem;
    transition:
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .ctrl-btn:hover {
    border-color: var(--color-border-accent, rgba(242, 194, 106, 0.25));
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .ctrl-btn:focus-visible {
    outline: none;
    border-color: var(--color-border-accent, rgba(242, 194, 106, 0.25));
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .ctrl-btn.is-active {
    border-color: var(--color-border-accent, rgba(242, 194, 106, 0.25));
    background: var(--color-surface-4, #1c2235);
    color: var(--color-text-accent, #f2c26a);
    box-shadow: 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
  }

  .ctrl-label {
    display: none;
  }

  @media (min-width: 520px) {
    .ctrl-label {
      display: inline;
    }
  }

  .ctrl-icon {
    width: 2rem;
    justify-content: center;
    padding: 0;
  }

  :global(.dir-arrow) {
    transition: transform var(--duration-normal) var(--ease-mechanical);
  }

  :global(.dir-arrow.is-up) {
    transform: rotate(180deg);
  }

  .sort-menu {
    position: absolute;
    left: 0;
    top: calc(100% + 0.3rem);
    z-index: 50;
    min-width: 10rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.98);
    backdrop-filter: blur(var(--glass-blur-lg));
    -webkit-backdrop-filter: blur(var(--glass-blur-lg));
    border-radius: var(--radius-sm, 6px);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    padding: 0.3rem 0;
    overflow: hidden;
  }

  .sort-menu-item {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    width: calc(100% - 0.4rem);
    margin: 0 0.2rem;
    padding: 0.45rem 0.65rem;
    border-radius: var(--radius-xs, 4px);
    background: transparent;
    border: 1px solid transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    letter-spacing: 0.04em;
    text-align: left;
    transition:
      background-color var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default);
  }

  .sort-menu-item:hover {
    background: rgb(255 255 255 / 0.04);
    border-color: var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    color: var(--color-text-primary);
  }

  .sort-menu-item.is-active {
    background: linear-gradient(90deg, rgb(242 194 106 / 0.12), transparent);
    border-color: rgb(242 194 106 / 0.18);
    color: var(--color-text-accent, #c49a5a);
  }

  .filter-count {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 1.05rem;
    min-width: 1.05rem;
    border: 1px solid rgb(242 194 106 / 0.48);
    border-radius: var(--radius-xs, 4px);
    background:
      linear-gradient(180deg, rgb(255 255 255 / 0.08), transparent 48%),
      linear-gradient(180deg, rgb(45 34 16 / 0.96), rgb(18 15 10 / 0.92));
    color: var(--color-text-accent-bright, #f5d48a);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    font-weight: 700;
    letter-spacing: 0;
    line-height: 1;
    box-shadow:
      inset 0 1px 0 rgb(255 255 255 / 0.10),
      inset 0 -1px 0 rgb(0 0 0 / 0.55),
      0 0 0 1px rgb(242 194 106 / 0.12),
      0 0 14px rgb(242 194 106 / 0.24);
    padding: 0 0.25rem;
    text-shadow: 0 0 8px rgb(242 194 106 / 0.55);
  }

  .thumb-size-inline {
    display: none;
  }

  .thumb-size-compact {
    display: inline-flex;
  }

  @media (min-width: 520px) {
    .thumb-size-inline {
      display: inline-flex;
    }
    .thumb-size-compact {
      display: none;
    }
  }

  .thumb-size-inline {
    align-items: center;
    gap: 0.45rem;
    padding: 0 0.55rem;
    height: 2rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-1, #0c0f15);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-muted);
  }

  .thumb-size-inline :global(.thumb-size-icon) {
    color: var(--color-text-disabled);
    flex-shrink: 0;
  }

  .thumb-size-inline :global(.thumb-size-icon-min) {
    width: 0.78rem;
    height: 0.78rem;
  }

  .thumb-size-inline :global(.thumb-size-icon-max) {
    width: 0.9rem;
    height: 0.9rem;
    transform: rotate(180deg);
  }

  .thumb-size-inline input {
    width: 5rem;
    height: 14px;
    appearance: none;
    -webkit-appearance: none;
    background: transparent;
  }

  .thumb-size-inline input::-webkit-slider-runnable-track {
    height: 2px;
    background: linear-gradient(
      to right,
      rgb(242 194 106 / 0.5),
      rgb(242 194 106 / 0.05)
    );
    box-shadow: inset 0 0 4px rgb(0 0 0 / 0.6);
  }

  .thumb-size-inline input::-moz-range-track {
    height: 2px;
    background: linear-gradient(
      to right,
      rgb(242 194 106 / 0.5),
      rgb(242 194 106 / 0.05)
    );
  }

  .thumb-size-inline input::-webkit-slider-thumb {
    width: 11px;
    height: 11px;
    margin-top: -4.5px;
    appearance: none;
    -webkit-appearance: none;
    border: 1px solid rgb(244 220 170);
    border-radius: 50%;
    background: radial-gradient(circle at 30% 30%, #f3e6cc, #b8862e 65%);
    box-shadow:
      0 0 6px rgb(242 194 106 / 0.55),
      0 0 12px rgb(242 194 106 / 0.25),
      inset 0 1px 0 rgb(255 255 255 / 0.3);
  }

  .thumb-size-inline input::-moz-range-thumb {
    width: 11px;
    height: 11px;
    border: 1px solid rgb(244 220 170);
    border-radius: 50%;
    background: radial-gradient(circle at 30% 30%, #f3e6cc, #b8862e 65%);
    box-shadow:
      0 0 6px rgb(242 194 106 / 0.55),
      0 0 12px rgb(242 194 106 / 0.25);
  }

  .thumb-size-inline input:focus-visible {
    outline: none;
  }

  .thumb-size-inline input:focus-visible::-webkit-slider-thumb {
    box-shadow:
      0 0 0 3px rgb(242 194 106 / 0.25),
      0 0 12px rgb(242 194 106 / 0.4);
  }

  .thumb-size-popover {
    position: absolute;
    right: 0;
    top: calc(100% + 0.3rem);
    z-index: 50;
    display: flex;
    align-items: center;
    gap: 0.55rem;
    width: min(13rem, calc(100vw - 4rem));
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.98);
    backdrop-filter: blur(var(--glass-blur-lg));
    -webkit-backdrop-filter: blur(var(--glass-blur-lg));
    border-radius: var(--radius-sm, 6px);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    padding: 0.7rem 0.8rem;
  }

  .thumb-size-popover :global(.thumb-size-icon) {
    color: var(--color-text-disabled);
    flex-shrink: 0;
  }

  .thumb-size-popover :global(.thumb-size-icon-min) {
    width: 0.85rem;
    height: 0.85rem;
  }

  .thumb-size-popover :global(.thumb-size-icon-max) {
    width: 1rem;
    height: 1rem;
    transform: rotate(180deg);
  }

  .thumb-size-popover input {
    flex: 1 1 auto;
    min-width: 0;
    width: auto;
    height: 28px;
    appearance: none;
    -webkit-appearance: none;
    background: transparent;
  }

  .thumb-size-popover input::-webkit-slider-runnable-track {
    height: 3px;
    background: linear-gradient(
      to right,
      rgb(242 194 106 / 0.5),
      rgb(242 194 106 / 0.05)
    );
    box-shadow: inset 0 0 4px rgb(0 0 0 / 0.6);
  }

  .thumb-size-popover input::-moz-range-track {
    height: 3px;
    background: linear-gradient(
      to right,
      rgb(242 194 106 / 0.5),
      rgb(242 194 106 / 0.05)
    );
  }

  .thumb-size-popover input::-webkit-slider-thumb {
    width: 18px;
    height: 18px;
    margin-top: -7.5px;
    appearance: none;
    -webkit-appearance: none;
    border: 1px solid rgb(244 220 170);
    border-radius: 50%;
    background: radial-gradient(circle at 30% 30%, #f3e6cc, #b8862e 65%);
    box-shadow:
      0 0 6px rgb(242 194 106 / 0.55),
      0 0 12px rgb(242 194 106 / 0.25),
      inset 0 1px 0 rgb(255 255 255 / 0.3);
  }

  .thumb-size-popover input::-moz-range-thumb {
    width: 18px;
    height: 18px;
    border: 1px solid rgb(244 220 170);
    border-radius: 50%;
    background: radial-gradient(circle at 30% 30%, #f3e6cc, #b8862e 65%);
    box-shadow:
      0 0 6px rgb(242 194 106 / 0.55),
      0 0 12px rgb(242 194 106 / 0.25);
  }

  .thumb-size-popover input:focus-visible {
    outline: none;
  }

  .thumb-size-popover input:focus-visible::-webkit-slider-thumb {
    box-shadow:
      0 0 0 3px rgb(242 194 106 / 0.25),
      0 0 12px rgb(242 194 106 / 0.4);
  }

  .view-toggle {
    display: inline-flex;
    align-items: center;
    height: 2rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-1, #0c0f15);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    overflow: hidden;
  }

  .view-toggle button {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 100%;
    width: 2rem;
    background: transparent;
    color: var(--color-text-muted);
    border: 1px solid transparent;
    border-radius: var(--radius-xs, 4px);
    transition:
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .view-toggle button:not(:disabled):hover {
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
  }

  .view-toggle button.is-active {
    background: var(--color-surface-4, #1c2235);
    color: var(--color-text-accent, #f2c26a);
  }

  /*
   * Active-filters / Clear bar — the first lower bar. It pulls up 1px behind
   * the hero (border-top dropped, only bottom corners rounded) so the seam
   * between it and the hero reads as one continuous panel, matching the
   * EntityDetail tab strip.
   */
  .filter-row {
    z-index: 1;
    gap: 0.4rem;
    min-height: 2.1rem;
    padding: calc(0.4rem + var(--toolbar-bar-overlap)) 0.7rem 0.4rem;
    pointer-events: auto;
  }

  .filter-scroll {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    flex: 1 1 auto;
    min-width: 0;
    overflow-x: auto;
    scrollbar-width: thin;
  }

  .filter-reset {
    flex: 0 0 auto;
    height: 1.6rem;
    min-height: 1.6rem;
    margin-left: auto;
  }

  .filter-chip-label {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    flex-shrink: 0;
    color: var(--color-text-disabled);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    font-weight: 600;
    letter-spacing: 0.16em;
  }

  .filter-chip {
    display: inline-flex;
    flex: 0 0 auto;
    align-items: center;
    gap: 0.4rem;
    height: 1.6rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    line-height: 1;
    padding: 0 0.6rem;
    transition:
      border-color var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default),
      background var(--duration-fast) var(--ease-default),
      box-shadow var(--duration-fast) var(--ease-default);
  }

  .filter-chip:hover {
    border-color: var(--color-error-border, rgba(168, 72, 80, 0.4));
    background: var(--color-surface-3, #151a28);
    color: var(--color-error-text, #cc7880);
    box-shadow: 0 0 0 1px rgba(168, 72, 80, 0.3), 0 0 8px rgba(168, 72, 80, 0.15);
  }

  .filter-chip span {
    max-width: 12rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .sr-only {
    position: absolute;
    width: 1px;
    height: 1px;
    margin: -1px;
    padding: 0;
    border: 0;
    overflow: hidden;
    clip: rect(0 0 0 0);
    white-space: nowrap;
  }

  /*
   * On narrow viewports labels collapse to icons (below 520px) so the row
   * fits in one line. The trailing cluster keeps `margin-left: auto` to
   * stay flush right — never stranded against the left edge with empty
   * space to its right.
   */
  @media (max-width: 520px) {
    .toolbar-hero {
      padding: 0.8rem 0.75rem;
    }
  }
</style>
