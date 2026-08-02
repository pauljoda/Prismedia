<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import type { PlaybackStatisticsEntity } from "$lib/api/generated/model";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { labelForEntityKind, resolveEntityHref } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { formatActiveDuration, statNumber } from "$lib/stats/playback-stats";
  import { formatRelativeTime } from "$lib/utils/format";

  interface Props {
    entities: PlaybackStatisticsEntity[];
    thumbnailFor: (entity: PlaybackStatisticsEntity) => EntityThumbnailCard;
    class?: string;
  }

  let { entities, thumbnailFor, class: className }: Props = $props();

  /** Rails are drawn relative to the leader so first place always reads as a full bar. */
  const leaderEvents = $derived(
    Math.max(
      1,
      ...entities.map((entity) => statNumber(entity.accessedCount)),
    ),
  );

  /**
   * A leaderboard mixes posters, square album art, and wide video stills. Squaring every card
   * keeps the title column aligned down the whole list instead of stepping in and out.
   */
  function squared(card: EntityThumbnailCard): EntityThumbnailCard {
    return { ...card, aspectRatio: "square" };
  }
</script>

<ol class={cn("board", className)}>
  {#each entities as entity, index (entity.id)}
    {@const accessed = statNumber(entity.accessedCount)}
    {@const completed = statNumber(entity.completedCount)}
    {@const skipped = statNumber(entity.skippedCount)}
    {@const accent = entityAccentForKind(entity.kind)}
    {@const href = resolveEntityHref(entity.kind, entity.id)}
    {@const card = thumbnailFor(entity)}
    <li>
      <svelte:element
        this={href ? "a" : "div"}
        href={href ?? undefined}
        class={cn("board-row", href && "board-row-link")}
        style:--band-primary={accent.primary}
        style:--band-secondary={accent.secondary}
      >
        <span class="board-rank">{index + 1}</span>

        <span class="board-thumb">
          <EntityThumbnail card={squared(card)} imageLoading="lazy" interactive={false} mediaOnly />
        </span>

        <span class="board-main">
          <span class="board-title">{entity.title}</span>
          <span class="board-meta">
            {labelForEntityKind(entity.kind)} · {formatRelativeTime(entity.lastEventAt, true)}
          </span>
          <span class="board-rail" aria-hidden="true">
            <span class="board-rail-fill" style:width={`${(accessed / leaderEvents) * 100}%`}></span>
          </span>
        </span>

        <span class="board-figures">
          <span class="board-watch">{formatActiveDuration(statNumber(entity.activeSeconds))}</span>
          <span class="board-counts">
            {accessed.toLocaleString()} opened · {completed.toLocaleString()} completed{#if skipped > 0} · {skipped.toLocaleString()} skipped{/if}
          </span>
        </span>
      </svelte:element>
    </li>
  {/each}
</ol>

<style>
  .board {
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .board-row {
    display: grid;
    grid-template-columns: 1.35rem auto minmax(0, 1fr) auto;
    align-items: center;
    gap: 0.7rem;
    padding: 0.45rem 0.75rem;
    border-top: 1px solid var(--color-border-subtle);
    text-decoration: none;
    transition: background var(--duration-fast, 120ms) var(--ease-default, ease);
  }

  .board > li:first-child .board-row {
    border-top: none;
  }

  .board-row-link:hover {
    background: var(--color-surface-2);
  }

  .board-rank {
    font-family: var(--font-mono);
    font-size: 0.7rem;
    font-variant-numeric: tabular-nums;
    text-align: right;
    color: var(--color-text-disabled);
  }

  .board-row-link:hover .board-rank {
    color: var(--color-text-muted);
  }

  .board-thumb {
    width: 3.1rem;
    height: 3.1rem;
    flex: 0 0 auto;
  }

  .board-thumb :global(.entity-thumbnail) {
    width: 100%;
    height: 100%;
  }

  .board-main {
    display: flex;
    flex-direction: column;
    gap: 0.18rem;
    min-width: 0;
  }

  .board-title {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.82rem;
    font-weight: 500;
    color: var(--color-text-primary);
  }

  .board-meta {
    font-family: var(--font-mono);
    font-size: 0.62rem;
    color: var(--color-text-disabled);
  }

  .board-rail {
    display: block;
    height: 2px;
    margin-top: 0.1rem;
    border-radius: 1px;
    background: var(--color-surface-3);
    overflow: hidden;
  }

  .board-rail-fill {
    display: block;
    height: 100%;
    min-width: 2px;
    border-radius: 1px;
    background: linear-gradient(90deg, var(--band-primary), var(--band-secondary));
  }

  .board-figures {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    gap: 0.15rem;
    text-align: right;
    white-space: nowrap;
  }

  .board-watch {
    font-family: var(--font-mono);
    font-size: 0.78rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-primary);
  }

  .board-counts {
    font-family: var(--font-mono);
    font-size: 0.6rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-text-disabled);
  }

  @media (max-width: 30rem) {
    .board-row {
      grid-template-columns: 1.2rem auto minmax(0, 1fr);
      gap: 0.55rem;
      padding: 0.45rem 0.6rem;
    }

    .board-figures {
      grid-column: 2 / span 2;
      align-items: flex-start;
      flex-direction: row;
      gap: 0.5rem;
      text-align: left;
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .board-row {
      transition: none;
    }
  }
</style>
