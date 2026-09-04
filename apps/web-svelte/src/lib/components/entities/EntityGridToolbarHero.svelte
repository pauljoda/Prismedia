<script lang="ts">
  import {
    ChevronsDownUp,
    ChevronsUpDown,
    SlidersHorizontal,
  } from "@lucide/svelte";
  import { Badge, Button, cn } from "@prismedia/ui-svelte";
  import type { FilterPreset } from "$lib/filter-presets";
  import type {
    EntityGridSort,
    EntityGridSortDir,
    EntityGridViewMode,
  } from "$lib/entities/entity-grid";
  import EntityGridPresetDropdown from "./EntityGridPresetDropdown.svelte";
  import EntityGridToolbarSearch from "./EntityGridToolbarSearch.svelte";
  import EntityGridToolbarViewControls from "./EntityGridToolbarViewControls.svelte";

  interface Props {
    activeFilterCount: number;
    activePresetId: string | null;
    barsCollapsed: boolean;
    drawerOpen: boolean;
    enableFeedView: boolean;
    entityKind?: string;
    hasCollapsibleRows: boolean;
    maxScale: number;
    mediaWall: boolean;
    minScale: number;
    onApplyPreset: (preset: FilterPreset) => void;
    onDeletePreset: (id: string) => void;
    onDrawerOpenChange: (open: boolean) => void;
    onMediaWallChange: (mediaWall: boolean) => void;
    onOverwritePreset: (id: string) => void;
    onQueryChange: (query: string) => void;
    onReshuffle: () => void;
    onSavePreset: (name: string) => void;
    onScaleChange: (scale: number) => void;
    onSortByChange: (sortBy: EntityGridSort) => void;
    onSortDirChange: (sortDir: EntityGridSortDir) => void;
    onToggleCollapse: () => void;
    onViewModeChange: (viewMode: EntityGridViewMode) => void;
    presets: FilterPreset[];
    query: string;
    scale: number;
    sortBy: EntityGridSort;
    sortDir: EntityGridSortDir;
    viewMode: EntityGridViewMode;
  }

  let {
    activeFilterCount,
    activePresetId,
    barsCollapsed,
    drawerOpen,
    enableFeedView,
    entityKind,
    hasCollapsibleRows,
    maxScale,
    mediaWall,
    minScale,
    onApplyPreset,
    onDeletePreset,
    onDrawerOpenChange,
    onMediaWallChange,
    onOverwritePreset,
    onQueryChange,
    onReshuffle,
    onSavePreset,
    onScaleChange,
    onSortByChange,
    onSortDirChange,
    onToggleCollapse,
    onViewModeChange,
    presets,
    query,
    scale,
    sortBy,
    sortDir,
    viewMode,
  }: Props = $props();
</script>

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
      <Button
        variant="ghost"
        class={cn("ctrl-btn ctrl-filters", drawerOpen && "is-active")}
        aria-label="Filters"
        aria-expanded={drawerOpen}
        onclick={() => onDrawerOpenChange(!drawerOpen)}
      >
        <SlidersHorizontal class="h-3.5 w-3.5" />
        <span class="ctrl-label">Filters</span>
        {#if activeFilterCount > 0}
          <Badge class="filter-count">{activeFilterCount}</Badge>
        {/if}
      </Button>

      <EntityGridPresetDropdown
        {activePresetId}
        {presets}
        {onApplyPreset}
        {onSavePreset}
        {onOverwritePreset}
        {onDeletePreset}
      />

      {#if hasCollapsibleRows}
        <Button
          variant="ghost"
          class={cn("ctrl-btn ctrl-icon collapse-toggle", barsCollapsed && "is-active")}
          title={barsCollapsed ? "Show filter and selection rows" : "Hide filter and selection rows"}
          aria-label={barsCollapsed ? "Show filter and selection rows" : "Hide filter and selection rows"}
          aria-expanded={!barsCollapsed}
          onclick={onToggleCollapse}
        >
          {#if barsCollapsed}
            <ChevronsUpDown class="h-3.5 w-3.5" />
          {:else}
            <ChevronsDownUp class="h-3.5 w-3.5" />
          {/if}
        </Button>
      {/if}
    </div>
  </div>
</div>

<style>
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

  .toolbar-hero :global(.ctrl-btn) {
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

  .toolbar-hero :global(.ctrl-btn:hover) {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
    box-shadow: inset 0 0 0 1px var(--color-border-default);
  }

  .toolbar-hero :global(.ctrl-btn:focus-visible) {
    outline: none;
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    box-shadow: var(--shadow-focus-accent);
  }

  .toolbar-hero :global(.ctrl-btn.is-active) {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-4, #1c2235);
    color: var(--color-text-accent, #c7c9cc);
    box-shadow: inset 0 -2px 0 var(--entity-accent, var(--color-accent-500));
  }

  .toolbar-hero :global(.ctrl-label) {
    display: none;
  }

  .toolbar-hero :global(.ctrl-icon) {
    width: 2rem;
    justify-content: center;
    padding: 0;
  }

  .toolbar-hero :global(.filter-count) {
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
    .toolbar-hero :global(.ctrl-label) {
      display: inline;
    }
  }

  @media (max-width: 520px) {
    .toolbar-hero {
      padding: 0.8rem 0.75rem;
    }
  }
</style>
