<script lang="ts">
  import { LayoutGrid, List, Rows3, X } from "@lucide/svelte";
  import { buttonVariants, Popover, Separator, Slider, Toggle, ToggleGroup } from "@prismedia/ui-svelte";
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
  let { enableFeedView = false, maxScale, mediaWall, minScale, onMediaWallChange, onScaleChange, onViewModeChange, scale, viewMode }: Props = $props();
  const id = $props.id();
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

<Popover.Root>
  <Popover.Trigger class={buttonVariants({ variant: "secondary", size: "md" })} aria-label="Display options">
    <LayoutGrid class="size-4" />Display
  </Popover.Trigger>
  <Popover.Content align="end" aria-labelledby={`${id}-title`} aria-describedby={`${id}-description`}>
    <Popover.Header>
      <div class="flex items-center justify-between gap-2">
        <Popover.Title id={`${id}-title`}>Display options</Popover.Title>
        <Popover.Close class={buttonVariants({ variant: "ghost", size: "icon" })} aria-label="Close display options"><X class="size-4" /></Popover.Close>
      </div>
      <Popover.Description id={`${id}-description`}>Choose how you browse this library.</Popover.Description>
    </Popover.Header>
    <ToggleGroup.Root type="single" variant="outline" spacing={1} class="w-full" aria-label="View mode"
      bind:value={() => viewMode, selectView}>
      {#each views as view (view.value)}
        <ToggleGroup.Item value={view.value} aria-label={`${view.label} view`} class="flex-1">
          <view.icon />{view.label}
        </ToggleGroup.Item>
      {/each}
    </ToggleGroup.Root>
    {#if viewMode !== ENTITY_GRID_VIEW_MODE.list}
      <Separator />
      <div class="flex items-center justify-between gap-3">
        <span id={`${id}-density`} class="text-sm">Thumbnail columns</span>
        <span class="font-mono text-xs tabular-nums text-text-muted">{scale}</span>
      </div>
      <Slider type="single" min={minScale} max={maxScale} step={1} bind:value={() => scale, onScaleChange}
        thumbLabel="Thumbnail columns" aria-labelledby={`${id}-density`} />
      <div class="flex justify-between text-xs text-text-muted"><span>Larger artwork</span><span>More items</span></div>
      <Separator />
      <div class="flex items-center justify-between gap-4">
        <div class="flex flex-col gap-1">
          <label for={`${id}-wall`} class="text-sm">Artwork only</label>
          <p id={`${id}-wall-description`} class="text-xs text-text-muted">Hide titles and details below each cover.</p>
        </div>
        <Toggle id={`${id}-wall`} checked={mediaWall} onchange={onMediaWallChange} ariaLabel="Artwork only" ariaDescribedby={`${id}-wall-description`} />
      </div>
    {/if}
  </Popover.Content>
</Popover.Root>
