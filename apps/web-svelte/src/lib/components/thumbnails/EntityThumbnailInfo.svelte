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
  <div class="glass-info" class:has-subtitle={Boolean(card.subtitle || subtitleContent)}>
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
  .glass-info {
    position: relative;
    z-index: 1;
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: 0.25rem;
    min-width: 0;
    padding: 0.5rem 0.6rem;
    border-top: 1px solid rgb(255 255 255 / 0.05);
    background: rgb(16 16 18 / 0.98);
    overflow: hidden;
    pointer-events: none;
  }

  .glass-info.has-subtitle { gap: 0.15rem; }

  .copy { display: flex; flex-direction: column; min-width: 0; max-width: 100%; }

  h3 {
    display: -webkit-box;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
    line-clamp: 2;
    margin: 0;
    min-width: 0;
    overflow: hidden;
    color: rgb(244 239 230 / 0.95);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.82rem;
    font-weight: 600;
    line-height: 1.3;
    letter-spacing: -0.01em;
    text-overflow: ellipsis;
    white-space: normal;
  }

  .title-size-compact { font-size: 0.72rem; font-weight: 600; line-height: 1.2; }
  .title-align-left { text-align: left; }
  .title-align-center { text-align: center; }
  .title-align-right { text-align: right; }

  .subtitle {
    overflow: hidden;
    margin: 0.1rem 0 0;
    color: rgb(196 201 212 / 0.82);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    line-height: 1.25;
    text-overflow: ellipsis;
    white-space: nowrap;
    text-shadow: 0 1px 3px rgb(0 0 0 / 0.6);
  }

  .custom-subtitle { display: flex; min-width: 0; margin-top: 0.22rem; }
  .custom-subtitle.title-align-left { justify-content: flex-start; }
  .custom-subtitle.title-align-center { justify-content: center; }
  .custom-subtitle.title-align-right { justify-content: flex-end; }

  .chips { display: flex; flex-wrap: wrap; gap: 0.25rem; margin-top: 0.1rem; max-block-size: 1.4rem; overflow: hidden; }
  .chip {
    display: inline-flex;
    align-items: center;
    gap: 0.2rem;
    min-width: 0;
    max-width: 100%;
    min-height: 1.1rem;
    overflow: hidden;
    border: 1px solid rgb(255 255 255 / 0.1);
    border-radius: var(--radius-xs, 4px);
    background: rgb(255 255 255 / 0.06);
    color: rgb(244 239 230 / 0.72);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    line-height: 1;
    padding: 0.12rem 0.28rem;
    text-overflow: ellipsis;
    text-shadow: 0 1px 2px rgb(0 0 0 / 0.5);
    white-space: nowrap;
  }
  .chip :global(svg) { flex: 0 0 auto; color: var(--color-text-muted); }

  :global(.entity-thumbnail.is-list) .glass-info {
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
  :global(.entity-thumbnail.is-list.is-compact) .glass-info { min-block-size: 3.25rem; padding: 0.4rem 0.55rem; }

  @container (max-width: 220px) {
    .glass-info { gap: 0.15rem; padding: 0.35rem 0.45rem; }
    h3 { font-size: 0.72rem; }
    .chips { gap: 0.15rem; max-block-size: 1.15rem; }
    .chip { min-height: 0.9rem; padding: 0.08rem 0.2rem; font-size: 0.52rem; }
    .subtitle { display: none; }
  }

  @container (max-width: 140px) {
    .glass-info { gap: 0.1rem; padding: 0.25rem 0.35rem; }
    h3 { -webkit-line-clamp: 1; line-clamp: 1; font-size: 0.62rem; }
    .subtitle, .chips { display: none; }
  }
</style>
