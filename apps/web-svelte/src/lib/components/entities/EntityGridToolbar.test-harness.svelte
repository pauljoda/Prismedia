<script lang="ts">
  import type { ComponentProps } from "svelte";
  import EntityGridToolbar from "./EntityGridToolbar.svelte";

  let { barsCollapsed = false, onBarsCollapsedChange = () => {}, canClearFiltersAndSort = true }:
    Partial<Pick<ComponentProps<typeof EntityGridToolbar>, "barsCollapsed" | "onBarsCollapsedChange" | "canClearFiltersAndSort">> = $props();
  let selectionActive = $state(false);

  const noop = () => {};
</script>

<EntityGridToolbar
  activeFilterIds={[]}
  allSelectedNsfw={false}
  {barsCollapsed}
  bulkActions={[]}
  collectionItems={[]}
  {canClearFiltersAndSort}
  drawerOpen={false}
  filterOptions={[]}
  maxScale={12}
  minScale={2}
  onActiveFilterIdsChange={noop}
  onApplyPreset={noop}
  {onBarsCollapsedChange}
  onClearFiltersAndSort={noop}
  onClearSelection={noop}
  onDeletePreset={noop}
  onDrawerOpenChange={noop}
  onMediaWallChange={noop}
  onOverwritePreset={noop}
  onQueryChange={noop}
  onReshuffle={noop}
  onSavePreset={noop}
  onScaleChange={noop}
  onSelectAllVisible={noop}
  onSelectionActiveChange={(active) => (selectionActive = active)}
  onSortByChange={noop}
  onSortDirChange={noop}
  onToggleNsfwFlag={noop}
  onViewModeChange={noop}
  presets={[]}
  mediaWall={false}
  query=""
  scale={6}
  selectable
  selectedCount={0}
  selectedIds={[]}
  {selectionActive}
  sortBy="title"
  sortDir="asc"
  viewMode="grid"
/>
