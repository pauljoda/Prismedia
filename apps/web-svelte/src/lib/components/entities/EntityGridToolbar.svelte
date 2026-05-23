<script lang="ts">
  import {
    ArrowUpDown,
    Check,
    ChevronDown,
    Grid2x2,
    Grid3x3,
    LayoutGrid,
    List,
    RotateCcw,
    Search,
    SlidersHorizontal,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { FilterPreset } from "$lib/filter-presets";
  import { entityGridFilterFromId } from "$lib/entities/entity-grid";
  import type {
    EntityGridFilterOption,
    EntityGridSort,
    EntityGridSortDir,
    EntityGridViewMode,
  } from "$lib/entities/entity-grid";
  import EntityGridPresetDropdown from "./EntityGridPresetDropdown.svelte";

  interface Props {
    activeFilterIds: string[];
    activePresetId?: string | null;
    canClearFiltersAndSort: boolean;
    drawerOpen: boolean;
    filterOptions: EntityGridFilterOption[];
    maxScale: number;
    minScale: number;
    onActiveFilterIdsChange: (ids: string[]) => void;
    onApplyPreset: (preset: FilterPreset) => void;
    onClearFiltersAndSort: () => void;
    onDeletePreset: (id: string) => void;
    onDrawerOpenChange: (open: boolean) => void;
    onOverwritePreset: (id: string) => void;
    onQueryChange: (query: string) => void;
    onSavePreset: (name: string) => void;
    onScaleChange: (scale: number) => void;
    onSortByChange: (sortBy: EntityGridSort) => void;
    onSortDirChange: (sortDir: EntityGridSortDir) => void;
    onViewModeChange: (viewMode: EntityGridViewMode) => void;
    presets: FilterPreset[];
    query: string;
    scale: number;
    selectedCount: number;
    sortBy: EntityGridSort;
    sortDir: EntityGridSortDir;
    viewMode: EntityGridViewMode;
  }

  let {
    activeFilterIds,
    activePresetId = null,
    canClearFiltersAndSort,
    drawerOpen,
    filterOptions,
    maxScale,
    minScale,
    onActiveFilterIdsChange,
    onApplyPreset,
    onClearFiltersAndSort,
    onDeletePreset,
    onDrawerOpenChange,
    onOverwritePreset,
    onQueryChange,
    onSavePreset,
    onScaleChange,
    onSortByChange,
    onSortDirChange,
    onViewModeChange,
    presets,
    query,
    scale,
    selectedCount,
    sortBy,
    sortDir,
    viewMode,
  }: Props = $props();

  const SORT_LABELS: Record<EntityGridSort, string> = {
    title: "Title",
    kind: "Kind",
    position: "Position",
    rating: "Rating",
  };

  const SORT_OPTIONS: { value: EntityGridSort; label: string }[] = [
    { value: "title", label: "Title" },
    { value: "kind", label: "Kind" },
    { value: "position", label: "Position" },
    { value: "rating", label: "Rating" },
  ];

  let sortOpen = $state(false);
  let thumbSizeOpen = $state(false);

  const activeFilters = $derived(
    activeFilterIds
      .map((id) => entityGridFilterFromId(id, filterOptions))
      .filter((option): option is EntityGridFilterOption => Boolean(option)),
  );

  function removeFilter(id: string) {
    onActiveFilterIdsChange(activeFilterIds.filter((filterId) => filterId !== id));
  }

  function parseScale(event: Event) {
    onScaleChange(Number((event.currentTarget as HTMLInputElement).value));
  }
</script>

<div class="toolbar-shell">
  <div class="toolbar-root">
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
    </div>

    <div class="controls-row">
      <div class="control-cluster">
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
            <div class="sort-menu">
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

        <button
          type="button"
          class="ctrl-btn ctrl-icon"
          title={sortDir === "asc" ? "Ascending — click to reverse" : "Descending — click to reverse"}
          aria-label={`Sort direction: ${sortDir}`}
          onclick={() => onSortDirChange(sortDir === "asc" ? "desc" : "asc")}
        >
          <ChevronDown class={cn("h-3.5 w-3.5 dir-arrow", sortDir === "asc" && "is-up")} />
        </button>

        <span class="cluster-divider" aria-hidden="true"></span>

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
        </div>

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
            <div class="thumb-size-popover">
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

        {#if canClearFiltersAndSort}
          <button
            type="button"
            title="Clear filters, sort, search, and saved preferences"
            class="ctrl-btn ctrl-clear"
            onclick={onClearFiltersAndSort}
          >
            <RotateCcw class="h-3.5 w-3.5 shrink-0" />
            <span class="ctrl-label">Clear</span>
          </button>
        {/if}
      </div>
    </div>

    <div class="filter-scroll" class:is-active={activeFilters.length > 0} aria-live="polite">
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
    content: "";
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    height: 0.5rem;
    background: var(--color-bg, #07080b);
    z-index: -1;
    pointer-events: auto;
  }

  .toolbar-root {
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.96);
    backdrop-filter: blur(16px);
    -webkit-backdrop-filter: blur(16px);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    border-radius: var(--radius-sm, 6px);
    padding: 0.7rem 0.75rem;
    pointer-events: auto;
  }

  .search-row {
    display: flex;
    align-items: center;
    gap: 0.65rem;
    min-width: 0;
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

  .cluster-divider {
    display: inline-block;
    width: 1px;
    height: 1.1rem;
    background: linear-gradient(
      to bottom,
      transparent,
      rgb(255 255 255 / 0.08),
      transparent
    );
    margin: 0 0.15rem;
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
    backdrop-filter: blur(24px);
    -webkit-backdrop-filter: blur(24px);
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
    height: 1rem;
    min-width: 1rem;
    border: 1px solid rgb(242 194 106 / 0.4);
    background: linear-gradient(180deg, rgb(242 194 106 / 0.9), rgb(184 134 46 / 0.95));
    color: #1a1408;
    font-size: 0.58rem;
    font-weight: 700;
    letter-spacing: 0;
    box-shadow: 0 0 10px rgb(242 194 106 / 0.35);
    padding: 0 0.25rem;
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
    backdrop-filter: blur(24px);
    -webkit-backdrop-filter: blur(24px);
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

  .filter-scroll {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    overflow-x: auto;
    padding: 0;
    scrollbar-width: thin;
    pointer-events: auto;
  }

  .filter-scroll.is-active {
    min-height: 2rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-1, #0c0f15);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    padding: 0.3rem 0.5rem;
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
    .toolbar-root {
      padding: 0.6rem 0.6rem;
    }
  }
</style>
