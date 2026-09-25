<script lang="ts">
  import type { Component } from "svelte";
  import EntityShelf from "$lib/components/entities/EntityShelf.svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  interface Props {
    label: string;
    icon?: Component;
    /** Cards in recency order; the most recent card always stays first. */
    cards: EntityThumbnailCard[];
    href?: string | null;
  }

  let { label, icon, cards, href = null }: Props = $props();

  interface RailPosition {
    first: boolean;
    last: boolean;
    /** Where this card's slice of the run's gradient starts and ends, 0 through 100. */
    from: number;
    to: number;
    groupLabel: string;
    groupSize: number;
  }

  function cardKey(card: EntityThumbnailCard): string {
    return `${card.entity.kind}:${card.entity.id}`;
  }

  /** Paints this card's slice of one continuous primary-to-secondary gradient across the whole run. */
  function railPaint(primary: string, secondary: string, position: RailPosition | undefined): string {
    const from = position?.from ?? 0;
    const to = position?.to ?? 100;
    return `linear-gradient(90deg, color-mix(in oklab, ${secondary} ${from}%, ${primary}), color-mix(in oklab, ${secondary} ${to}%, ${primary}))`;
  }

  /**
   * Gathers the mixed rail into runs of one kind so each run sits under a single colour rail. Runs
   * are ordered by their most recent card, and cards keep recency order inside a run, so the first
   * card is still the thing you touched last.
   */
  const grouped = $derived.by(() => {
    const runs = new Map<string, EntityThumbnailCard[]>();
    for (const card of cards) runs.set(card.entity.kind, [...(runs.get(card.entity.kind) ?? []), card]);
    const ordered: EntityThumbnailCard[] = [];
    const positions = new Map<string, RailPosition>();
    for (const [kind, run] of runs) {
      run.forEach((card, index) => {
        ordered.push(card);
        positions.set(cardKey(card), {
          first: index === 0,
          last: index === run.length - 1,
          from: (index / run.length) * 100,
          to: ((index + 1) / run.length) * 100,
          groupLabel: labelForEntityKind(kind),
          groupSize: run.length,
        });
      });
    }
    return { ordered, positions };
  });
</script>

<EntityShelf {label} {icon} {href} cards={grouped.ordered} sizing="height">
  {#snippet item(card)}
    {@const position = grouped.positions.get(cardKey(card))}
    {@const accent = entityAccentForKind(card.entity.kind)}
    <div class="flex flex-col gap-2">
      <div class="flex h-4 items-end">
        {#if position?.first}
          <span class="whitespace-nowrap font-mono text-[0.64rem] uppercase tracking-[0.14em] text-text-muted">
            {position.groupLabel}<span class="text-text-disabled"> · {position.groupSize}</span>
          </span>
        {/if}
      </div>
      <!-- The rail runs through the gap to the next card of the same kind, so each run reads as one band. -->
      <span
        class="family-rail"
        class:is-run-end={position?.last ?? true}
        style:background={railPaint(accent.primary, accent.secondary, position)}
        aria-hidden="true"
      ></span>
      <EntityThumbnail {card} artworkReactive={false} />
    </div>
  {/snippet}
</EntityShelf>

<style>
  .family-rail {
    display: block;
    height: 2px;
    margin-right: -0.75rem;
    border-radius: 1px;
    opacity: 0.85;
  }

  .family-rail.is-run-end {
    margin-right: 0;
  }
</style>
