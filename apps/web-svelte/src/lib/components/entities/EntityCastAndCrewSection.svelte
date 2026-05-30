<script lang="ts">
  import { Building2, Users } from "@lucide/svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";

  interface Props {
    studioCards?: EntityThumbnailCard[];
    creditCards?: EntityThumbnailCard[];
    studioLabel?: string;
    castLabel?: string;
  }

  let {
    studioCards = [],
    creditCards = [],
    studioLabel = "Studios",
    castLabel = "Cast",
  }: Props = $props();

  const hasStudios = $derived(studioCards.length > 0);
  const hasCredits = $derived(creditCards.length > 0);
  const hasContent = $derived(hasStudios || hasCredits);

  function thumbnailKey(card: EntityThumbnailCard): string {
    return `${card.entity.kind}:${card.entity.id}:${card.subtitle ?? ""}`;
  }
</script>

{#if hasContent}
  <div class="credit-rows">
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
    gap: 1rem;
    min-width: 0;
  }

  .credit-row {
    display: grid;
    gap: 0.55rem;
    min-width: 0;
    overflow: hidden;
  }

  .credit-row-label {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    margin: 0;
    color: var(--color-text-secondary, #c4c9d4);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
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
    border: 1px solid rgb(255 255 255 / 0.14);
    border-radius: var(--radius-xs, 4px);
    background: linear-gradient(180deg, rgb(18 20 24 / 0.96), rgb(9 10 12 / 0.98));
    color: rgb(196 201 212 / 0.88);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.58rem;
    line-height: 1.16;
    padding: 0.18rem 0.3rem;
    overflow-wrap: anywhere;
    text-align: center;
    white-space: normal;
    text-shadow: 0 1px 3px rgb(0 0 0 / 0.6);
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
