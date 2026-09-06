<script lang="ts">
  import { Badge } from "@prismedia/ui-svelte";
  import { Flame, Star } from "@lucide/svelte";
  import { getRatingValue, isNsfw, isWanted } from "$lib/api/capabilities";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  let { card, inline = false, selectable = false, showWantedBadge = true }: {
    card: EntityThumbnailCard;
    inline?: boolean;
    selectable?: boolean;
    showWantedBadge?: boolean;
  } = $props();

  const wanted = $derived(showWantedBadge && isWanted(card.entity.capabilities));
  const status = $derived(acquisitionStatusDisplay(card.wantedStatus));
  const nsfw = $derived(isNsfw(card.entity.capabilities));
  const rating = $derived(getRatingValue(card.entity.capabilities));
  const position = $derived(card.custom?.bottomLeft);
  const source = $derived(card.custom?.sourceTag);
  const ratingLabel = $derived(String(Math.round(rating)));
</script>

{#if wanted || nsfw || rating > 0 || position || source}
  <div class="thumbnail-badges" class:is-inline={inline} class:has-selection={selectable}>
    <div class="top-row">
      {#if position}
        <div class="badges bottom-left-badges" class:has-selection={selectable}>
          <Badge variant="secondary" class="position-badge" title={position.title ?? position.label}>{position.label}</Badge>
        </div>
      {/if}
      {#if wanted || nsfw}
        <div class="badges top-badges">
          {#if wanted}
            {@const StatusIcon = status.icon}
            <Badge variant="secondary" class="wanted-badge" data-tone={status.tone} title={`Wanted — ${status.label}`} aria-label={`Wanted — ${status.label}`}>
              <StatusIcon data-icon="inline-start" />
              <span class="wanted-label">{status.label}</span>
            </Badge>
          {/if}
          {#if nsfw}
            <Badge variant="secondary" class="danger" title="NSFW" aria-label="NSFW"><Flame data-icon="inline-start" />{#if inline}NSFW{/if}</Badge>
          {/if}
        </div>
      {/if}
    </div>
    <div class="bottom-row">
      {#if source}
        <div class="badges source-badges"><Badge variant="secondary" class="source-badge" title={source.title ?? source.label}>{source.label}</Badge></div>
      {/if}
      {#if rating > 0}
        <div class="badges bottom-right-badges"><Badge variant="secondary" class="rating-badge" title={`Rating ${ratingLabel}`} aria-label={`Rating ${ratingLabel}`}>{ratingLabel}<Star data-icon="inline-end" /></Badge></div>
      {/if}
    </div>
  </div>
{/if}

<style>
  .thumbnail-badges { position: absolute; inset: 0; z-index: 3; pointer-events: none; }
  .top-row, .bottom-row { position: absolute; inset-inline: calc(var(--spacing) * 2); display: flex; flex-wrap: wrap; align-items: start; gap: var(--spacing-control-gap-sm); }
  .top-row { top: calc(var(--spacing) * 2); }
  .bottom-row { bottom: calc(var(--spacing) * 2); }
  .has-selection .top-row { inset-inline-start: calc(var(--spacing) * 10); }
  .badges { display: flex; flex-wrap: wrap; min-width: 0; gap: var(--spacing-control-gap-sm); }
  .top-badges, .bottom-right-badges { margin-inline-start: auto; justify-content: flex-end; }
  .badges :global([data-slot="badge"]) { max-width: 100%; }
  .wanted-label { overflow: hidden; text-overflow: ellipsis; }
  .thumbnail-badges :global(.danger svg), .thumbnail-badges :global([data-tone="failed"] svg) { color: var(--color-error-text); }
  .thumbnail-badges :global([data-tone="attention"] svg) { color: var(--color-warning-text); }
  .thumbnail-badges :global([data-tone="done"] svg) { color: var(--color-success-text); }
  .thumbnail-badges :global(.rating-badge svg) { fill: currentColor; }

  .is-inline { position: static; display: flex; flex-wrap: wrap; gap: var(--spacing-control-gap-sm); margin-top: var(--spacing-control-gap-sm); }
  .is-inline .top-row, .is-inline .bottom-row, .is-inline .badges { display: contents; }

  @container (max-width: 112px) {
    .thumbnail-badges:not(.is-inline) .wanted-label { display: none; }
  }
</style>
