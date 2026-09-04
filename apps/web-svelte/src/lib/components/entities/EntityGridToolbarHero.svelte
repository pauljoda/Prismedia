<script lang="ts">
  import {
    ChevronsDownUp,
    ChevronsUpDown,
    SlidersHorizontal,
    ListChecks,
    X,
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
    selectable: boolean;
    selectionActive: boolean;
    onSelectionActiveChange: (active: boolean) => void;
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
    selectable,
    selectionActive,
    onSelectionActiveChange,
    presets,
    query,
    scale,
    sortBy,
    sortDir,
    viewMode,
  }: Props = $props();
</script>

<div class="toolbar-hero">
  <EntityGridToolbarSearch {entityKind} {onQueryChange} {onReshuffle} {onSortByChange} {onSortDirChange} {query} {sortBy} {sortDir} />
  <div class="toolbar-actions">
    <EntityGridToolbarViewControls {enableFeedView} {maxScale} {mediaWall} {minScale} {onMediaWallChange} {onScaleChange} {onViewModeChange} {scale} {viewMode} />
    <Button variant={drawerOpen ? "secondary" : "ghost"} aria-expanded={drawerOpen} onclick={() => onDrawerOpenChange(!drawerOpen)}>
      <SlidersHorizontal class="size-4" />Filters
      {#if activeFilterCount > 0}<Badge>{activeFilterCount}</Badge>{/if}
    </Button>
    <EntityGridPresetDropdown {activePresetId} {presets} {onApplyPreset} {onSavePreset} {onOverwritePreset} {onDeletePreset} />
    {#if selectable}
      <Button variant={selectionActive ? "secondary" : "ghost"} aria-pressed={selectionActive}
        aria-label={selectionActive ? "Exit selection" : "Select items"} onclick={() => onSelectionActiveChange(!selectionActive)}>
        {#if selectionActive}<X class="size-4" />Done{:else}<ListChecks class="size-4" />Select{/if}
      </Button>
    {/if}
    {#if hasCollapsibleRows}
      <Button variant="ghost" size="icon" aria-expanded={!barsCollapsed}
        aria-label={barsCollapsed ? "Show filter and selection rows" : "Hide filter and selection rows"} onclick={onToggleCollapse}>
        {#if barsCollapsed}<ChevronsUpDown class="size-4" />{:else}<ChevronsDownUp class="size-4" />{/if}
      </Button>
    {/if}
  </div>
</div>

<style>
  .toolbar-hero {
    position: relative;
    z-index: 3;
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.5rem 0.75rem;
    min-width: 0;
    padding: 0.75rem;
    border: 1px solid var(--toolbar-detail-border);
    border-top: 2px solid color-mix(in srgb, var(--toolbar-page-accent) 65%, var(--toolbar-detail-border));
    border-radius: var(--radius-sm);
    background: var(--toolbar-detail-glass);
  }
  .toolbar-actions {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.375rem;
    margin-left: auto;
    min-width: 0;
  }
</style>
