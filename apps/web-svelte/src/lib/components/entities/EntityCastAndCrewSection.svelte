<script lang="ts">
  import { Building2, Users, type LucideIcon } from "@lucide/svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";

  interface Props {
    studioCards?: EntityThumbnailCard[];
    creditCards?: EntityThumbnailCard[];
    /** Optional leading row of related entities (e.g. the series an episode belongs to). */
    relatedCards?: EntityThumbnailCard[];
    studioLabel?: string;
    castLabel?: string;
    relatedLabel?: string;
    relatedIcon?: LucideIcon;
  }

  let {
    studioCards = [],
    creditCards = [],
    relatedCards = [],
    studioLabel = "Studios",
    castLabel = "Cast",
    relatedLabel = "Related",
    relatedIcon,
  }: Props = $props();

  const hasStudios = $derived(studioCards.length > 0);
  const hasCredits = $derived(creditCards.length > 0);
  const hasRelated = $derived(relatedCards.length > 0);
  const hasContent = $derived(hasStudios || hasCredits || hasRelated);

  function thumbnailKey(card: EntityThumbnailCard): string {
    return `${card.entity.kind}:${card.entity.id}:${card.subtitle ?? ""}`;
  }
</script>

{#if hasContent}
  <div class="credit-rows">
    {#if hasRelated}
      <section class="credit-row" aria-label={relatedLabel}>
        <h3 class="credit-row-label">
          {#if relatedIcon}
            {@const RelatedIcon = relatedIcon}
            <RelatedIcon class="h-3.5 w-3.5" />
          {/if}
          {relatedLabel}
        </h3>
        <div class="credit-scroller">
          {#each relatedCards as thumbnailCard (thumbnailKey(thumbnailCard))}
            <div class="credit-thumbnail">
              <EntityThumbnail card={thumbnailCard} selectable={false} titleAlign="center" titleSize="compact" />
            </div>
          {/each}
        </div>
      </section>
    {/if}

    {#if hasStudios}
      <section class="credit-row" aria-label={studioLabel}>
        <h3 class="credit-row-label">
          <Building2 class="h-3.5 w-3.5" />
          {studioLabel}
        </h3>
        <div class="credit-scroller">
          {#each studioCards as thumbnailCard (thumbnailKey(thumbnailCard))}
            <div class="credit-thumbnail is-studio">
              <EntityThumbnail card={thumbnailCard} selectable={false} titleAlign="center" titleSize="compact" />
            </div>
          {/each}
        </div>
      </section>
    {/if}

    {#if hasCredits}
      <section class="credit-row" aria-label={castLabel}>
        <h3 class="credit-row-label">
          <Users class="h-3.5 w-3.5" />
          {castLabel}
        </h3>
        <div class="credit-scroller">
          {#each creditCards as thumbnailCard (thumbnailKey(thumbnailCard))}
            <div class="credit-thumbnail">
              {#if thumbnailCard.subtitle}
                <EntityThumbnail card={thumbnailCard} selectable={false} titleAlign="center" titleSize="compact">
                  {#snippet subtitleContent(card)}
                    <span class="credit-role-label">{card.subtitle}</span>
                  {/snippet}
                </EntityThumbnail>
              {:else}
                <EntityThumbnail card={thumbnailCard} selectable={false} titleAlign="center" titleSize="compact" />
              {/if}
            </div>
          {/each}
        </div>
      </section>
    {/if}
  </div>
{/if}

<style>
  .credit-rows {
    display: grid;
    gap: 1.25rem;
    min-width: 0;
  }

  .credit-row {
    display: grid;
    gap: 0.7rem;
    min-width: 0;
    overflow: hidden;
  }

  .credit-row-label {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 0;
    color: var(--color-text, #f4efe6);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.875rem;
    font-weight: 600;
    letter-spacing: -0.01em;
  }

  .credit-row-label :global(svg) {
    color: var(--color-text-muted, #8a93a6);
  }

  .credit-scroller {
    display: flex;
    gap: 0.75rem;
    min-width: 0;
    max-width: 100%;
    overflow-x: auto;
    overflow-y: hidden;
    padding-bottom: 0.35rem;
    scroll-padding-inline: 0.25rem;
    scrollbar-width: thin;
  }

  .credit-thumbnail {
    flex: 0 0 clamp(7rem, 33vw, 8.75rem);
    min-width: 0;
  }

  .credit-thumbnail.is-studio {
    flex-basis: clamp(7.75rem, 34vw, 10rem);
  }

  .credit-role-label {
    display: flex;
    width: fit-content;
    max-width: 100%;
    min-width: 0;
    justify-content: center;
    overflow: visible;
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: 0.6875rem;
    font-weight: 500;
    line-height: 1.3;
    padding: 0.1rem 0.25rem;
    overflow-wrap: anywhere;
    text-align: center;
    white-space: normal;
  }

  @media (min-width: 640px) {
    .credit-thumbnail {
      flex-basis: 8.25rem;
    }

    .credit-thumbnail.is-studio {
      flex-basis: 10.5rem;
    }
  }
</style>
