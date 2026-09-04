<script lang="ts">
  import { Checkbox } from "@prismedia/ui-svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityThumbnailBadges from "./EntityThumbnailBadges.svelte";

  let { card, onSelectedChange, selectable, selected, showWantedBadge, showBadges = true }: {
    card: EntityThumbnailCard;
    onSelectedChange?: (selected: boolean) => void;
    selectable: boolean;
    selected: boolean;
    showWantedBadge: boolean;
    showBadges?: boolean;
  } = $props();
  const progressPercent = $derived(card.progress != null && card.progress > 0 ? Math.min(100, Math.max(0, card.progress * 100)) : null);
  function stopSelectionActivation(event: Event) { event.stopPropagation(); }
</script>

{#if selectable}
  <span class="selection">
    <Checkbox class="size-full" checked={selected} title={`Select ${card.entity.title}`} aria-label={`Select ${card.entity.title}`} onclick={stopSelectionActivation} onpointerdown={stopSelectionActivation} onchange={onSelectedChange} />
  </span>
{/if}
{#if showBadges}<EntityThumbnailBadges {card} {selectable} {showWantedBadge} />{/if}
{#if progressPercent != null}<div class="progress-meter" aria-hidden="true"><span class="progress-meter-fill" style:width={`${progressPercent}%`}></span></div>{/if}

<style>
  .progress-meter { position: absolute; inset: auto 0 0; z-index: 4; height: 3px; background: rgb(0 0 0 / 0.45); }
  .progress-meter-fill { display: block; height: 100%; background: color-mix(in srgb, var(--entity-accent) 80%, #c7c9cc); }
  .selection { position: absolute; z-index: 6; top: calc(var(--spacing) * 2); left: calc(var(--spacing) * 2); width: var(--spacing-control-xs); height: var(--spacing-control-xs); border-radius: var(--radius-xs); background: var(--color-surface-1); opacity: 0; pointer-events: none; transition: opacity 120ms ease; }
  :global(.entity-thumbnail:is(:hover, :focus-within)) .selection, :global(.entity-thumbnail.is-select-mode) .selection, :global(.entity-thumbnail.is-selected) .selection, .selection:focus-within { opacity: 1; pointer-events: auto; }
  :global(.entity-thumbnail.is-list) .selection { opacity: 1; pointer-events: auto; }
</style>
