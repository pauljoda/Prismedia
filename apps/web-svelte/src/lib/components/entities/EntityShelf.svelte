<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { ChevronRight } from "@lucide/svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { toAspectRatioNumeric, type EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  /**
   * Horizontal scrolling shelf of entity thumbnails with a standard header.
   *
   * Sizing modes:
   * - `width` (default): every card gets the same width and its height follows
   *   its own aspect ratio. Right for single-kind shelves where every card
   *   shares a shape.
   * - `height`: every card gets the same image height and its width follows its
   *   aspect ratio. Right for mixed-kind shelves (videos next to posters next
   *   to album squares) where uniform widths would make the row ragged.
   *
   * Customization is snippet-based: `headerAccessory` adds content beside the
   * "View all" link and `item` replaces the default thumbnail renderer per card.
   */
  interface Props {
    label: string;
    icon?: Component;
    cards: EntityThumbnailCard[];
    /** "View all" destination; omit to hide the link. */
    href?: string | null;
    sizing?: "width" | "height";
    headerAccessory?: Snippet;
    item?: Snippet<[EntityThumbnailCard]>;
  }

  const { label, icon: Icon, cards, href = null, sizing = "width", headerAccessory, item }: Props = $props();
  const shelfAccent = $derived(entityAccentForKind(cards[0]?.entity.kind));

  function itemWidthStyle(card: EntityThumbnailCard): string {
    if (sizing === "width") return "clamp(140px, 18vw, 220px)";
    return `calc(var(--shelf-h) * ${toAspectRatioNumeric(card.aspectRatio).toFixed(4)})`;
  }
</script>

<section
  style:--entity-accent={shelfAccent.primary}
  style:--entity-accent-secondary={shelfAccent.secondary}
>
  <div class="flex items-center justify-between mb-4 px-3">
    <h2 class="text-lg font-semibold flex items-center gap-2">
      <span class="shelf-marker" aria-hidden="true"></span>
      {#if Icon}
        <span class="shelf-icon"><Icon class="w-4.5 h-4.5" /></span>
      {/if}
      {label}
    </h2>
    <div class="flex items-center gap-3">
      {@render headerAccessory?.()}
      {#if href}
        <a
          {href}
          class="shelf-link inline-flex items-center gap-1 text-xs text-text-muted transition-colors"
        >
          View all
          <ChevronRight class="h-3.5 w-3.5" />
        </a>
      {/if}
    </div>
  </div>

  <div
    class="flex gap-3 overflow-x-auto pt-1 pb-5 snap-x snap-mandatory scrollbar-hidden px-3"
    style:--shelf-h={sizing === "height" ? "clamp(150px, 16vw, 200px)" : undefined}
  >
    {#each cards as card (card.entity.id)}
      <div class="flex-none snap-start" style:width={itemWidthStyle(card)}>
        {#if item}
          {@render item(card)}
        {:else}
          <EntityThumbnail {card} />
        {/if}
      </div>
    {/each}
  </div>
</section>

<style>
  .shelf-marker {
    width: 0.8rem;
    height: 2px;
    flex: 0 0 auto;
    background: color-mix(in srgb, var(--entity-accent) 74%, #c7c9cc);
  }

  .shelf-icon {
    color: var(--color-text-muted);
  }

  .shelf-link:hover {
    color: var(--color-text-primary);
  }
</style>
