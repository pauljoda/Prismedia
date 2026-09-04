<script lang="ts">
  import { Building2, Users, type LucideIcon } from "@lucide/svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import EntityShelf from "./EntityShelf.svelte";

  interface Props {
    studioCards?: EntityThumbnailCard[];
    creditCards?: EntityThumbnailCard[];
    /** Related entities, such as the series containing an episode. */
    relatedCards?: EntityThumbnailCard[];
    studioLabel?: string;
    castLabel?: string;
    relatedLabel?: string;
    relatedIcon?: LucideIcon;
  }

  let {
    studioCards = [], creditCards = [], relatedCards = [],
    studioLabel = "Studios", castLabel = "Cast", relatedLabel = "Related", relatedIcon,
  }: Props = $props();
</script>

{#snippet thumbnail(card: EntityThumbnailCard)}
  <EntityThumbnail {card} selectable={false} titleAlign="left" titleSize="compact">
    {#snippet subtitleContent(card)}
      {#if card.subtitle}<span class="credit-role">{card.subtitle}</span>{/if}
    {/snippet}
  </EntityThumbnail>
{/snippet}

{#if relatedCards.length || studioCards.length || creditCards.length}
  <div class="grid min-w-0 gap-5">
    {#if relatedCards.length}
      <EntityShelf label={relatedLabel} icon={relatedIcon} cards={relatedCards} compact item={thumbnail} />
    {/if}
    {#if studioCards.length}
      <EntityShelf label={studioLabel} icon={Building2} cards={studioCards} compact item={thumbnail} />
    {/if}
    {#if creditCards.length}
      <EntityShelf label={castLabel} icon={Users} cards={creditCards} compact item={thumbnail} />
    {/if}
  </div>
{/if}

<style>
  .credit-role { color: var(--color-text-muted); font-size: 0.75rem; line-height: 1.4; overflow-wrap: anywhere; }
</style>
