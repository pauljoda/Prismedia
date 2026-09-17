<script lang="ts">
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  /** Uses the library's artwork, aspect ratios and keyboard activation for remote results. */
  let { cards, onActivate, disabled = false }: {
    cards: EntityThumbnailCard[];
    onActivate: (card: EntityThumbnailCard) => void;
    disabled?: boolean;
  } = $props();
</script>

<div class="discovery-grid" aria-label="Browse results" aria-busy={disabled}>
  {#each cards as card (card.entity.id)}
    <EntityThumbnail {card} linkable={false} artworkReactive={false} showBadges={false}
      interactive={!disabled} onActivate={() => onActivate(card)} />
  {/each}
</div>

<style>
  .discovery-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 9rem), 1fr));
    gap: 1.75rem 1rem;
    align-items: start;
  }
  @media (min-width: 640px) {
    .discovery-grid { grid-template-columns: repeat(auto-fill, minmax(11rem, 1fr)); gap: 2rem 1.25rem; }
  }
</style>
