<script lang="ts">
  import {
    ChevronsDownUp,
    ChevronsUpDown,
    SlidersHorizontal,
  } from "@lucide/svelte";
  import { onMount } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
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
  import EntityGridToolbarActiveFilters from "./EntityGridToolbarActiveFilters.svelte";
  import EntityGridPresetDropdown from "./EntityGridPresetDropdown.svelte";
  import EntityGridToolbarSearch from "./EntityGridToolbarSearch.svelte";
  import EntityGridToolbarViewControls from "./EntityGridToolbarViewControls.svelte";
  import { EntityGridToolbarCollapseController } from "./entity-grid-toolbar-collapse-controller.svelte";

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

  // A persisted value seeds this mount; later prop changes must not overwrite a
  // manual in-session choice or the controller's scroll-driven compact state.
  // svelte-ignore state_referenced_locally
  const collapse = new EntityGridToolbarCollapseController(
    initialBarsCollapsed,
    (collapsed) => onBarsCollapsedChange?.(collapsed),
  );

  onMount(() => collapse.connectScroll());

</script>

<div class="toolbar-shell">
  <div class="toolbar-stack">
    <div class="toolbar-hero">
      <EntityGridToolbarSearch
        {entityKind}
        {onQueryChange}
        {onReshuffle}
        {onSortByChange}
        {onSortDirChange}
        {query}
        {sortBy}
        {sortDir}
      />

    <div class="controls-row">
      <div class="control-cluster">
        <EntityGridToolbarViewControls
          {enableFeedView}
          {maxScale}
          {mediaWall}
          {minScale}
          {onMediaWallChange}
          {onScaleChange}
          {onViewModeChange}
          {scale}
          {viewMode}
        />
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
            class:is-active={collapse.barsCollapsed}
            title={collapse.barsCollapsed ? "Show filter and selection rows" : "Hide filter and selection rows"}
            aria-label={collapse.barsCollapsed ? "Show filter and selection rows" : "Hide filter and selection rows"}
            aria-expanded={!collapse.barsCollapsed}
            onclick={() => collapse.toggle()}
          >
            {#if collapse.barsCollapsed}
              <ChevronsUpDown class="h-3.5 w-3.5" />
            {:else}
              <ChevronsDownUp class="h-3.5 w-3.5" />
            {/if}
          </button>
        {/if}
      </div>
    </div>
    </div>

    {#if !collapse.barsCollapsed && (activeFilters.length > 0 || canClearFiltersAndSort)}
      <EntityGridToolbarActiveFilters
        {activeFilterIds}
        {activeFilters}
        {canClearFiltersAndSort}
        {onActiveFilterIdsChange}
        {onClearFiltersAndSort}
      />
    {/if}

    {#if selectable && !collapse.barsCollapsed}
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
  .toolbar-shell {
    position: sticky;
    top: var(--prismedia-canvas-header-height, 3.5rem);
    z-index: 4;
    display: flex;
    flex-direction: column;
    padding-top: 0.5rem;
    background: transparent;
    pointer-events: none;

    --toolbar-detail-border: var(--color-border, #1c2235);
    --toolbar-detail-glass: rgb(12 15 21);
    --toolbar-detail-slideout-inset: 5px;
    --toolbar-bar-overlap: 0.5rem;
    --toolbar-page-accent: var(--page-accent, var(--entity-accent, #739b96));
  }

  .toolbar-shell::before {
    display: none;
  }

  .toolbar-stack {
    display: flex;
    flex-direction: column;
    min-width: 0;
    pointer-events: auto;
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
    height: 2px;
    background: linear-gradient(
      to right,
      transparent 0%,
      color-mix(in srgb, var(--toolbar-page-accent) 46%, transparent) 12%,
      color-mix(in srgb, var(--toolbar-page-accent) 78%, #c7c9cc) 50%,
      color-mix(in srgb, var(--toolbar-page-accent) 46%, transparent) 88%,
      transparent 100%
    );
    pointer-events: none;
  }

  .toolbar-stack :global(.toolbar-bar) {
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

  .toolbar-stack :global(.ctrl-btn) {
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

  .toolbar-stack :global(.ctrl-btn:hover) {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
    box-shadow: inset 0 0 0 1px var(--color-border-default);
  }

  .toolbar-stack :global(.ctrl-btn:focus-visible) {
    outline: none;
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    box-shadow: var(--shadow-focus-accent);
  }

  .toolbar-stack :global(.ctrl-btn.is-active) {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-4, #1c2235);
    color: var(--color-text-accent, #c7c9cc);
    box-shadow: inset 0 -2px 0 var(--entity-accent, var(--color-accent-500));
  }

  .toolbar-stack :global(.ctrl-label) {
    display: none;
  }

  .toolbar-stack :global(.ctrl-icon) {
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

  .filter-count {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 1.05rem;
    min-width: 1.05rem;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-3);
    color: var(--color-text-accent-bright, #d8d9dc);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    font-weight: 700;
    letter-spacing: 0;
    line-height: 1;
    box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.05);
    padding: 0 0.25rem;
    text-shadow: none;
  }

  @media (min-width: 520px) {
    .toolbar-stack :global(.ctrl-label) {
      display: inline;
    }
  }

  @media (max-width: 520px) {
    .toolbar-hero {
      padding: 0.8rem 0.75rem;
    }
  }
</style>
