<script lang="ts">
  import { Grid2x2, Grid3x3, Image, LayoutGrid, List, Rows3 } from "@lucide/svelte";
  import { Button, cn, Popover, Slider, ToggleGroup } from "@prismedia/ui-svelte";
  import { ENTITY_GRID_VIEW_MODE, type EntityGridViewMode } from "$lib/entities/entity-grid";

  interface Props {
    enableFeedView?: boolean;
    maxScale: number;
    mediaWall: boolean;
    minScale: number;
    onMediaWallChange: (mediaWall: boolean) => void;
    onScaleChange: (scale: number) => void;
    onViewModeChange: (viewMode: EntityGridViewMode) => void;
    scale: number;
    viewMode: EntityGridViewMode;
  }

  let {
    enableFeedView = false,
    maxScale,
    mediaWall,
    minScale,
    onMediaWallChange,
    onScaleChange,
    onViewModeChange,
    scale,
    viewMode,
  }: Props = $props();

  const views = $derived([
    { value: ENTITY_GRID_VIEW_MODE.grid, label: "Grid", icon: LayoutGrid },
    { value: ENTITY_GRID_VIEW_MODE.list, label: "List", icon: List },
    ...(enableFeedView ? [{ value: ENTITY_GRID_VIEW_MODE.feed, label: "Feed", icon: Rows3 }] : []),
  ]);

  function selectView(next: string) {
    const view = views.find((view) => view.value === next);
    if (view && view.value !== viewMode) onViewModeChange(view.value);
  }
</script>

<div class="view-controls">
  <ToggleGroup.Root type="single" class="view-toggle" aria-label="View mode"
    bind:value={() => viewMode, selectView}>
    {#each views as view (view.value)}
      <ToggleGroup.Item value={view.value} title={`${view.label} view`} aria-label={`${view.label} view`}>
        <view.icon class="size-3.5" />
      </ToggleGroup.Item>
    {/each}
  </ToggleGroup.Root>

  {#if viewMode !== ENTITY_GRID_VIEW_MODE.list}
    <Button variant="ghost" class={cn("ctrl-btn ctrl-icon", mediaWall && "is-active")}
      title="Media wall" aria-label="Media wall" aria-pressed={mediaWall}
      onclick={() => onMediaWallChange(!mediaWall)}>
      <Image class="size-3.5" />
    </Button>

    <div class="thumb-size-inline" title="Drag to change thumbnail size">
      <Grid2x2 class="size-3 shrink-0 text-text-disabled" aria-hidden="true" />
      <Slider type="single" min={minScale} max={maxScale} step={1}
        bind:value={() => scale, onScaleChange} thumbLabel="Thumbnail columns" class="min-h-0 w-20" />
      <Grid3x3 class="size-3.5 shrink-0 text-text-disabled" aria-hidden="true" />
    </div>

    <div class="thumb-size-compact">
      <Popover.Root>
        <Popover.Trigger class="ctrl-btn ctrl-icon" title="Thumbnail size" aria-label="Thumbnail size">
          <LayoutGrid class="size-3.5" />
        </Popover.Trigger>
        <Popover.Content align="end" aria-label="Thumbnail size" class="w-52 flex-row items-center gap-3 px-3 py-2">
          <Grid2x2 class="size-3.5 shrink-0 text-text-disabled" aria-hidden="true" />
          <Slider type="single" min={minScale} max={maxScale} step={1}
            bind:value={() => scale, onScaleChange} thumbLabel="Thumbnail columns" />
          <Grid3x3 class="size-4 shrink-0 text-text-disabled" aria-hidden="true" />
        </Popover.Content>
      </Popover.Root>
    </div>
  {/if}
</div>

<style>
  .view-controls {
    display: flex;
    align-items: center;
    gap: 0.3rem;
  }

  .thumb-size-inline {
    display: none;
    align-items: center;
    gap: 0.45rem;
    padding: 0 0.55rem;
    height: 2rem;
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
    border-radius: var(--radius-xs);
    box-shadow: inset 0 2px 8px rgb(0 0 0 / 0.3);
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

  .view-controls :global(.view-toggle) {
    display: inline-flex;
    align-items: center;
    height: 2rem;
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
    border-radius: var(--radius-xs);
    box-shadow: inset 0 2px 8px rgb(0 0 0 / 0.3);
  }

  .view-controls :global([data-slot="toggle-group-item"]) {
    height: 100%;
    width: 2rem;
    min-width: 2rem;
    padding: 0;
    border: 1px solid transparent;
    background: transparent;
    color: var(--color-text-muted);
  }

  .view-controls :global([data-slot="toggle-group-item"]:hover) {
    background: var(--color-surface-3);
    color: var(--color-text-primary);
  }

  .view-controls :global([data-slot="toggle-group-item"][data-state="on"]) {
    background: var(--color-surface-4);
    color: var(--color-text-accent);
  }
</style>
