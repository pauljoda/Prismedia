<script lang="ts">
  import { SearchX } from "@lucide/svelte";
  import PrismediaLoadingMark from "$lib/components/PrismediaLoadingMark.svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import type { EntityGridViewMode } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  interface Props {
    cardLinks: boolean;
    cards: EntityThumbnailCard[];
    emptyMessage: string;
    emptyTitle: string;
    hasVisibleCards: boolean;
    hoverPreviewSuppressed: () => boolean;
    loading: boolean;
    mediaWall: boolean;
    onCardActivate?: (card: EntityThumbnailCard, visibleCards: EntityThumbnailCard[]) => void;
    onCardSelectedChange: (id: string, selected: boolean) => void;
    selectable: boolean;
    selectedIds: string[];
    selectionActive: boolean;
    viewMode: EntityGridViewMode;
  }

  let {
    cardLinks,
    cards,
    emptyMessage,
    emptyTitle,
    hasVisibleCards,
    hoverPreviewSuppressed,
    loading,
    mediaWall,
    onCardActivate,
    onCardSelectedChange,
    selectable,
    selectedIds,
    selectionActive,
    viewMode,
  }: Props = $props();

  let FeedComponent = $state<typeof import("./EntityFeed.svelte").default | null>(null);

  $effect(() => {
    if (viewMode === "feed" && !FeedComponent) {
      void import("./EntityFeed.svelte").then((module) => (FeedComponent = module.default));
    }
  });
</script>

{#if loading && !hasVisibleCards}
  <div class="grid-loading">
    <PrismediaLoadingMark label="Loading entities" showLabel />
  </div>
{:else if hasVisibleCards && viewMode === "feed" && FeedComponent}
  <FeedComponent {cards} onActivate={onCardActivate} {mediaWall} />
{:else if hasVisibleCards}
  <div
    class="cards"
    class:is-list={viewMode === "list"}
    class:is-media-wall={mediaWall}
    aria-label="Entities"
  >
    {#each cards as card (card.entity.id)}
      <EntityThumbnail
        artworkReactive={false}
        {card}
        imageFetchPriority="auto"
        imageLoading="lazy"
        layout={viewMode === "feed" ? "grid" : viewMode}
        linkable={cardLinks && !onCardActivate}
        mediaOnly={mediaWall}
        onActivate={onCardActivate ? (activatedCard) => onCardActivate(activatedCard, cards) : undefined}
        {hoverPreviewSuppressed}
        selectable={selectable && selectionActive}
        selectMode={selectionActive}
        selected={selectedIds.includes(card.entity.id)}
        onSelectedChange={(selected) => onCardSelectedChange(card.entity.id, selected)}
      />
    {/each}
  </div>
{:else}
  <div class="empty" role="status">
    <span class="empty-icon">
      <SearchX aria-hidden="true" />
    </span>
    <strong>{emptyTitle}</strong>
    <span>{emptyMessage}</span>
  </div>
{/if}

<style>
  .cards {
    display: grid;
    grid-template-columns: repeat(
      max(1, min(calc(var(--col-count, 5) - 1), 4)),
      minmax(0, 1fr)
    );
    gap: 0.75rem;
    align-items: start;
    overflow-anchor: none;
    contain: layout;
    transition: grid-template-columns 240ms cubic-bezier(0.4, 0, 0.2, 1);
  }

  /*
   * Browser-native rendering virtualization keeps semantic cards available to find-in-page and
   * accessibility while skipping style, layout, and paint work well outside the viewport.
   */
  .cards :global(.entity-thumbnail) {
    content-visibility: auto;
    contain-intrinsic-block-size: auto 22rem;
  }

  .cards.is-list {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }

  .cards.is-list :global(.entity-thumbnail) {
    contain-intrinsic-block-size: auto 5.25rem;
  }

  .cards.is-media-wall {
    grid-template-columns: repeat(var(--col-count, 5), minmax(0, 1fr));
    gap: clamp(0.25rem, 0.8vw, 0.5rem);
  }

  .grid-loading {
    display: grid;
    min-height: clamp(16rem, 42vh, 28rem);
    place-items: center;
    overflow: hidden;
  }

  .empty {
    display: grid;
    gap: 0.35rem;
    min-height: 12rem;
    place-content: center;
    justify-items: center;
    padding: 2.5rem 1.25rem;
    background: var(--color-surface-1, #0c0f15);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-sm, 6px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-muted);
    text-align: center;
  }

  .empty > strong,
  .empty > span:not(.empty-icon) {
    max-width: 32rem;
  }

  .empty-icon {
    display: grid;
    place-items: center;
    justify-self: center;
    width: 2rem;
    height: 2rem;
    color: var(--color-text-disabled);
  }

  .empty-icon :global(svg) {
    width: 100%;
    height: 100%;
  }

  .empty strong {
    color: var(--color-text-primary);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.1rem;
  }

  .empty span {
    font-size: 0.85rem;
  }

  @media (min-width: 640px) {
    .cards {
      grid-template-columns: repeat(max(1, min(var(--col-count, 5), 4)), minmax(0, 1fr));
    }
  }

  @media (min-width: 1024px) {
    .cards {
      grid-template-columns: repeat(var(--col-count, 5), minmax(0, 1fr));
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .cards {
      transition: none;
    }
  }
</style>
