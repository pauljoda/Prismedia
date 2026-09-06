<script lang="ts">
  import {
    ChevronsDownUp,
    ChevronsUpDown,
    SlidersHorizontal,
  } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
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
        variant={drawerOpen ? "secondary" : "outline"}
        size="sm"
        aria-label="Filters"
        aria-expanded={drawerOpen}
        onclick={() => onDrawerOpenChange(!drawerOpen)}
      >
        <SlidersHorizontal class="h-3.5 w-3.5" />
        <span class="hidden min-[520px]:inline">Filters</span>
        {#if activeFilterCount > 0}
          <Badge class="h-4 px-1 text-[10px]">{activeFilterCount}</Badge>
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
          size="icon-sm"
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
    background: var(--color-card);
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

  @media (max-width: 520px) {
    .toolbar-hero {
      padding: 0.8rem 0.75rem;
    }
  }
</style>
