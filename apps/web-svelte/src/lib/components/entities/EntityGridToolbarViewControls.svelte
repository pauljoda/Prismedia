<script lang="ts">
  import {
    Grid2x2,
    Grid3x3,
    Image,
    LayoutGrid,
    List,
    Rows3,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import type { EntityGridViewMode } from "$lib/entities/entity-grid";

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

  let thumbSizeOpen = $state(false);

  function parseScale(event: Event) {
    onScaleChange(Number((event.currentTarget as HTMLInputElement).value));
  }
</script>

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
      <div class="floating-surface thumb-size-popover" use:keepFlyoutOnScreen>
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

<style>
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
      rgb(199 201 204 / 0.5),
      rgb(199 201 204 / 0.05)
    );
    box-shadow: inset 0 0 4px rgb(0 0 0 / 0.6);
  }

  .thumb-size-inline input::-moz-range-track {
    height: 2px;
    background: linear-gradient(
      to right,
      rgb(199 201 204 / 0.5),
      rgb(199 201 204 / 0.05)
    );
  }

  .thumb-size-inline input::-webkit-slider-thumb {
    width: 11px;
    height: 11px;
    margin-top: -4.5px;
    appearance: none;
    -webkit-appearance: none;
    border: 1px solid var(--color-border-default);
    border-radius: 50%;
    background: var(--color-accent-500);
    box-shadow: 0 1px 3px rgb(0 0 0 / 0.45);
  }

  .thumb-size-inline input::-moz-range-thumb {
    width: 11px;
    height: 11px;
    border: 1px solid var(--color-border-default);
    border-radius: 50%;
    background: var(--color-accent-500);
    box-shadow: 0 1px 3px rgb(0 0 0 / 0.45);
  }

  .thumb-size-inline input:focus-visible {
    outline: none;
  }

  .thumb-size-inline input:focus-visible::-webkit-slider-thumb {
    box-shadow: 0 0 0 3px rgb(199 201 204 / 0.25);
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
      rgb(199 201 204 / 0.5),
      rgb(199 201 204 / 0.05)
    );
    box-shadow: inset 0 0 4px rgb(0 0 0 / 0.6);
  }

  .thumb-size-popover input::-moz-range-track {
    height: 3px;
    background: linear-gradient(
      to right,
      rgb(199 201 204 / 0.5),
      rgb(199 201 204 / 0.05)
    );
  }

  .thumb-size-popover input::-webkit-slider-thumb {
    width: 18px;
    height: 18px;
    margin-top: -7.5px;
    appearance: none;
    -webkit-appearance: none;
    border: 1px solid var(--color-border-default);
    border-radius: 50%;
    background: var(--color-accent-500);
    box-shadow: 0 1px 3px rgb(0 0 0 / 0.45);
  }

  .thumb-size-popover input::-moz-range-thumb {
    width: 18px;
    height: 18px;
    border: 1px solid var(--color-border-default);
    border-radius: 50%;
    background: var(--color-accent-500);
    box-shadow: 0 1px 3px rgb(0 0 0 / 0.45);
  }

  .thumb-size-popover input:focus-visible {
    outline: none;
  }

  .thumb-size-popover input:focus-visible::-webkit-slider-thumb {
    box-shadow: 0 0 0 3px rgb(199 201 204 / 0.25);
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
    color: var(--color-text-accent, #c7c9cc);
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
</style>
