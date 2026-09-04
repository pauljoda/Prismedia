<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { ChevronLeft, ChevronRight } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
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
    /** Smaller related-entity shelf for detail pages. */
    compact?: boolean;
    headerAccessory?: Snippet;
    item?: Snippet<[EntityThumbnailCard]>;
  }

  const { label, icon: Icon, cards, href = null, sizing = "width", compact = false, headerAccessory, item }: Props = $props();
  const shelfAccent = $derived(entityAccentForKind(cards[0]?.entity.kind));
  const shelfId = $props.id();
  let scroller: HTMLDivElement | undefined;
  let canScrollBack = $state(false);
  let canScrollForward = $state(false);

  function observeScroll(node: HTMLDivElement) {
    scroller = node;
    function update() {
      canScrollBack = node.scrollLeft > 1;
      canScrollForward = node.scrollLeft + node.clientWidth < node.scrollWidth - 1;
    }
    const observer = new ResizeObserver(update);
    function observeItems() {
      observer.disconnect();
      observer.observe(node);
      for (const child of node.children) observer.observe(child);
      update();
    }
    const itemsObserver = new MutationObserver(observeItems);
    itemsObserver.observe(node, { childList: true });
    observeItems();
    node.addEventListener("scroll", update, { passive: true });
    update();
    return () => {
      observer.disconnect();
      itemsObserver.disconnect();
      node.removeEventListener("scroll", update);
      scroller = undefined;
    };
  }

  function scrollPage(direction: number) {
    if (!scroller) return;
    scroller.scrollBy({
      left: direction * scroller.clientWidth * 0.85,
      behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth",
    });
  }

  function itemWidthStyle(card: EntityThumbnailCard): string {
    if (sizing === "width") return compact ? "clamp(132px, 24vw, 156px)" : "clamp(140px, 18vw, 220px)";
    return `calc(var(--shelf-h) * ${toAspectRatioNumeric(card.aspectRatio).toFixed(4)})`;
  }
</script>

<section
  aria-label={label}
  class={compact ? "compact-shelf" : undefined}
  style:--entity-accent={shelfAccent.primary}
  style:--entity-accent-secondary={shelfAccent.secondary}
>
  <div class="shelf-header">
    <h2 class="shelf-title">
      {#if !compact}<span class="shelf-marker" aria-hidden="true"></span>{/if}
      {#if Icon}
        <span class="shelf-icon"><Icon class="w-4.5 h-4.5" /></span>
      {/if}
      {label}
      {#if compact}<Badge variant="secondary">{cards.length}</Badge>{/if}
    </h2>
    <div class="flex shrink-0 items-center gap-2">
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
      {#if canScrollBack || canScrollForward}
        <div class="flex gap-1">
          <Button variant="ghost" size="icon-sm" aria-label={`Previous ${label}`} aria-controls={shelfId} disabled={!canScrollBack} onclick={() => scrollPage(-1)}><ChevronLeft /></Button>
          <Button variant="ghost" size="icon-sm" aria-label={`Next ${label}`} aria-controls={shelfId} disabled={!canScrollForward} onclick={() => scrollPage(1)}><ChevronRight /></Button>
        </div>
      {/if}
    </div>
  </div>

  <div
    id={shelfId}
    {@attach observeScroll}
    class="shelf-items flex gap-3 overflow-x-auto pt-1 pb-3 snap-x snap-proximity"
    style:--shelf-h={sizing === "height" ? "clamp(150px, 16vw, 200px)" : undefined}
  >
    {#each cards as card (`${card.entity.kind}:${card.entity.id}:${card.subtitle ?? ""}`)}
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
  .shelf-header { display: flex; align-items: center; justify-content: space-between; gap: 0.75rem; min-height: 2rem; margin-bottom: 0.75rem; padding-inline: 0.75rem; }
  .shelf-title { display: flex; align-items: center; flex-wrap: wrap; gap: 0.5rem; margin: 0; font-family: var(--font-heading); font-size: 1.125rem; font-weight: 600; }
  .shelf-items { padding-inline: 0.75rem; scrollbar-width: thin; scroll-padding-inline: 0.75rem; }
  .compact-shelf { min-width: 0; }
  .compact-shelf .shelf-header, .compact-shelf .shelf-items { padding-inline: 0; }
  .compact-shelf .shelf-title { font-size: 0.875rem; }
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
