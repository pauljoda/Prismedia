<script lang="ts">
  import { Badge } from "@prismedia/ui-svelte";
  import OverflowTicker from "$lib/components/OverflowTicker.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { thumbnailMetaAccentForIcon } from "$lib/entities/entity-accent";
  import type { Snippet } from "svelte";
  import EntityThumbnailIcon from "./EntityThumbnailIcon.svelte";
  import EntityThumbnailBadges from "./EntityThumbnailBadges.svelte";

  interface Props {
    card: EntityThumbnailCard;
    mediaOnly: boolean;
    layout: "grid" | "list";
    showWantedBadge: boolean;
    subtitleContent?: Snippet<[EntityThumbnailCard]>;
    titleAlign: "left" | "center" | "right";
    titleSize: "default" | "compact";
  }

  let { card, mediaOnly, layout, showWantedBadge, subtitleContent, titleAlign, titleSize }: Props = $props();
  // Captions are a compact summary: two chips share one row, truncating long labels.
  const maxMetadataChips = 2;
</script>

{#if !mediaOnly}
  <div class="thumbnail-caption" class:has-subtitle={Boolean(card.subtitle || subtitleContent)}>
    <div class="copy">
      <h3 class={`title-align-${titleAlign} title-size-${titleSize}`} title={card.entity.title} aria-label={card.entity.title}>
        {card.entity.title}
      </h3>
      {#if subtitleContent}
        <div class={`custom-subtitle title-align-${titleAlign}`}>
          {@render subtitleContent(card)}
        </div>
      {/if}
      {#if card.subtitle && !subtitleContent}
        <div class={`subtitle title-align-${titleAlign}`} title={card.subtitle}>
          <OverflowTicker text={card.subtitle} align={titleAlign} />
        </div>
      {/if}
    </div>

    {#if layout === "list"}<EntityThumbnailBadges {card} {showWantedBadge} inline />{/if}

    {#if card.meta?.length}
      <div class="chips flex-nowrap">
        {#each card.meta.slice(0, maxMetadataChips) as item (item.icon + item.label)}
          <Badge
            variant="outline"
            class="chip min-w-0 max-w-full shrink"
            style={`--thumbnail-meta-accent: ${thumbnailMetaAccentForIcon(item.icon)}`}
            aria-label={`${item.icon} ${item.label}`}
            title={item.label}
          >
            <EntityThumbnailIcon icon={item.icon} />
            <span class="chip-label truncate">{item.label}</span>
          </Badge>
        {/each}
      </div>
    {/if}
  </div>
{/if}

<style>
  .thumbnail-caption {
    position: relative;
    z-index: 1;
    display: flex;
    flex-direction: column;
    gap: 0.2rem;
    min-width: 0;
    padding: 0 0.25rem;
    overflow: hidden;
    pointer-events: none;
  }

  .thumbnail-caption.has-subtitle { gap: 0.2rem; }

  .copy { display: flex; flex-direction: column; min-width: 0; max-width: 100%; }

  h3 {
    display: block;
    margin: 0;
    min-width: 0;
    overflow: hidden;
    color: var(--color-text, #f4efe6);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: var(--text-control);
    font-weight: 600;
    line-height: 1.25;
    letter-spacing: -0.01em;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .title-size-compact { font-size: var(--text-label); font-weight: 600; line-height: 1.25; }
  .title-align-left { text-align: left; }
  .title-align-center { text-align: center; }
  .title-align-right { text-align: right; }

  .subtitle {
    overflow: hidden;
    margin: 0;
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: var(--text-caption);
    line-height: 1.35;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .custom-subtitle { display: flex; min-width: 0; margin-top: 0.125rem; }
  .custom-subtitle.title-align-left { justify-content: flex-start; }
  .custom-subtitle.title-align-center { justify-content: center; }
  .custom-subtitle.title-align-right { justify-content: flex-end; }

  .chips { display: flex; gap: var(--spacing-control-gap-sm); margin-top: var(--spacing-control-gap-sm); }
  .chips :global(.chip svg) { flex-shrink: 0; color: var(--thumbnail-meta-accent); }

  :global(.entity-thumbnail.is-list) .thumbnail-caption {
    flex: 1 1 0;
    min-width: 0;
    min-height: auto;
    min-block-size: 5.25rem;
    height: auto;
    margin-top: 0;
    padding: 0.72rem 0.9rem;
    border: none;
    border-radius: 0;
    background: rgb(12 12 13 / 0.98);
    box-shadow: none;
  }
  :global(.entity-thumbnail.is-list.is-compact) .thumbnail-caption { min-block-size: 3.25rem; padding: 0.4rem 0.55rem; }

  @container (max-width: 220px) {
    .thumbnail-caption { gap: 0.125rem; padding: 0 0.25rem; }
    h3 { font-size: var(--text-label); }
    .chips { gap: var(--spacing); }
    .chips :global(.chip) {
      min-height: var(--spacing-badge-compact);
      padding-inline: var(--spacing);
      gap: var(--spacing);
    }
  }

  @container (max-width: 140px) {
    .thumbnail-caption { gap: 0.1rem; padding: 0 0.2rem; }
    h3 { font-size: var(--text-caption); }
    .subtitle { display: none; }
    /* Drop decoration before shrinking the shared badge text or losing its outline. */
    .chips :global(.chip svg) { display: none; }
  }
</style>
