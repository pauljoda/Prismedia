<script lang="ts">
  import OverflowTicker from "$lib/components/OverflowTicker.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import type { Snippet } from "svelte";
  import EntityThumbnailIcon from "./EntityThumbnailIcon.svelte";

  interface Props {
    card: EntityThumbnailCard;
    mediaOnly: boolean;
    subtitleContent?: Snippet<[EntityThumbnailCard]>;
    titleAlign: "left" | "center" | "right";
    titleSize: "default" | "compact";
  }

  let { card, mediaOnly, subtitleContent, titleAlign, titleSize }: Props = $props();
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

    {#if card.meta?.length}
      <div class="chips">
        {#each card.meta as item (item.icon + item.label)}
          <span class="chip" aria-label={`${item.icon} ${item.label}`}>
            <EntityThumbnailIcon icon={item.icon} />
            {item.label}
          </span>
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
    gap: 0.125rem;
    min-width: 0;
    padding: 0 0.25rem;
    overflow: hidden;
    pointer-events: none;
  }

  .thumbnail-caption.has-subtitle { gap: 0.125rem; }

  .copy { display: flex; flex-direction: column; min-width: 0; max-width: 100%; }

  h3 {
    display: block;
    margin: 0;
    min-width: 0;
    overflow: hidden;
    color: rgb(244 239 230 / 0.95);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.875rem;
    font-weight: 600;
    line-height: 1.25;
    letter-spacing: -0.01em;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .title-size-compact { font-size: 0.75rem; font-weight: 600; line-height: 1.2; }
  .title-align-left { text-align: left; }
  .title-align-center { text-align: center; }
  .title-align-right { text-align: right; }

  .subtitle {
    overflow: hidden;
    margin: 0;
    color: rgb(196 201 212 / 0.82);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6875rem;
    line-height: 1.25;
    text-overflow: ellipsis;
    white-space: nowrap;
    text-shadow: 0 1px 3px rgb(0 0 0 / 0.6);
  }

  .custom-subtitle { display: flex; min-width: 0; margin-top: 0.125rem; }
  .custom-subtitle.title-align-left { justify-content: flex-start; }
  .custom-subtitle.title-align-center { justify-content: center; }
  .custom-subtitle.title-align-right { justify-content: flex-end; }

  .chips { display: flex; flex-wrap: nowrap; gap: 0.25rem; margin-top: 0.125rem; overflow: hidden; }
  .chip {
    display: inline-flex;
    align-items: center;
    gap: 0.2rem;
    flex: 0 1 auto;
    min-width: 0;
    max-width: 100%;
    min-height: 1.15rem;
    overflow: hidden;
    border: 1px solid color-mix(in srgb, var(--entity-accent) 32%, rgb(255 255 255 / 0.12));
    border-radius: var(--radius-sm, 6px);
    background: color-mix(in srgb, var(--entity-accent) 11%, transparent);
    color: rgb(224 228 236 / 0.84);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.625rem;
    font-weight: 600;
    line-height: 1;
    padding: 0.125rem 0.25rem;
    text-overflow: ellipsis;
    text-shadow: 0 1px 2px rgb(0 0 0 / 0.5);
    white-space: nowrap;
  }
  .chip :global(svg) { flex: 0 0 auto; color: var(--color-text-muted); }

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
    h3 { font-size: 0.72rem; }
    .chips { gap: 0.15rem; }
    .chip { min-height: 0.9rem; padding: 0.08rem 0.2rem; font-size: 0.52rem; }
    .chip:nth-child(n + 4) { display: none; }
  }

  @container (max-width: 140px) {
    .thumbnail-caption { gap: 0.1rem; padding: 0 0.2rem; }
    h3 { font-size: 0.62rem; }
    .subtitle { display: none; }
    .chip:nth-child(n + 2) { display: none; }
  }
</style>
