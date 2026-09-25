<script lang="ts">
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { toAspectRatioNumeric, type EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { formatRelativeTime } from "$lib/utils/format";
  import { familyNoun, type LatestItem } from "./dashboard-data";
  import { mosaicSpanForAspect, packMosaic } from "./mosaic-layout";

  interface Props {
    items: LatestItem[];
  }

  let { items }: Props = $props();

  /** Narrowest tile, in pixels; the column count grows with the available width. */
  const MIN_TILE = 132;
  const GAP = 8;

  let width = $state(0);
  const columns = $derived(Math.max(3, Math.floor((width + GAP) / (MIN_TILE + GAP))));
  const cell = $derived(width > 0 ? (width - (columns - 1) * GAP) / columns : MIN_TILE);
  /** Rows are half a column tall, so squares, posters, and wide frames all land on whole row counts. */
  const rowUnit = $derived(cell / 2);
  /**
   * The band is a multiple of six half-rows, which squares (2) and posters (3) can always fill. Narrow
   * screens get a taller band so the featured tile still leaves room for others.
   */
  const bandRows = $derived(columns <= 4 ? 12 : 6);

  /** The newest arrival is featured at double size; everything else takes its artwork's footprint. */
  const tiles = $derived(
    packMosaic(
      items,
      (item, index) => {
        const span = mosaicSpanForAspect(toAspectRatioNumeric(item.card.aspectRatio));
        return index === 0 ? { columns: span.columns * 2, rows: span.rows * 2 } : span;
      },
      columns,
      bandRows,
    ),
  );

  function tileKey(card: EntityThumbnailCard): string {
    return `${card.entity.kind}:${card.entity.id}`;
  }
</script>

<div
  class="mosaic"
  bind:clientWidth={width}
  style:grid-template-columns="repeat({columns}, minmax(0, 1fr))"
  style:grid-template-rows="repeat({bandRows}, {rowUnit}px)"
  style:gap="{GAP}px"
>
  {#each tiles as tile, index (tileKey(tile.item.card))}
    {@const accent = entityAccentForKind(tile.item.card.entity.kind)}
    {@const featured = index === 0}
    <div
      class="tile"
      style:grid-column="{tile.column + 1} / span {tile.span.columns}"
      style:grid-row="{tile.row + 1} / span {tile.span.rows}"
    >
      <EntityThumbnail
        card={tile.item.card}
        mediaOnly
        artworkReactive={false}
        showBadges={false}
        imageFetchPriority={featured ? "high" : "low"}
      />
      <!-- Caption over the artwork: the thumbnail beneath stays the link, so the caption ignores the pointer. -->
      <div class="caption" class:is-featured={featured} aria-hidden="true">
        <span class="kind">
          <span class="marker" style:background="linear-gradient(90deg, {accent.primary}, {accent.secondary})"></span>
          {familyNoun(tile.item.card.entity.kind, 1)}
          {#if featured && tile.item.addedAt > 0}
            <span class="added">· added {formatRelativeTime(new Date(tile.item.addedAt).toISOString())}</span>
          {/if}
        </span>
        <span class="title">{tile.item.card.entity.title}</span>
      </div>
    </div>
  {/each}
</div>

<style>
  .mosaic {
    display: grid;
    width: 100%;
  }

  .tile {
    position: relative;
    min-width: 0;
    min-height: 0;
  }

  /* Tiles crop artwork to their footprint, so the thumbnail fills the cell instead of keeping its ratio. */
  .tile > :global(.entity-thumbnail),
  .tile :global(.media) {
    height: 100%;
    aspect-ratio: auto !important;
  }

  .caption {
    position: absolute;
    inset: auto 0 0;
    z-index: 5;
    display: flex;
    flex-direction: column;
    gap: 0.15rem;
    padding: 1.6rem 0.6rem 0.5rem;
    border-radius: 0 0 var(--radius-sm) var(--radius-sm);
    background: linear-gradient(180deg, transparent, rgb(5 5 6 / 0.72) 45%, rgb(5 5 6 / 0.92));
    pointer-events: none;
  }

  .caption.is-featured {
    gap: 0.3rem;
    padding: 3rem 1rem 0.9rem;
  }

  .kind {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    min-width: 0;
    overflow: hidden;
    font-family: var(--font-mono);
    font-size: 0.6rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    white-space: nowrap;
    color: var(--color-text-muted);
  }

  .marker {
    flex: 0 0 auto;
    width: 0.7rem;
    height: 2px;
    border-radius: 1px;
  }

  .added {
    letter-spacing: 0.04em;
    text-transform: none;
  }

  .title {
    overflow: hidden;
    font-size: 0.78rem;
    font-weight: 500;
    line-height: 1.25;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: var(--color-text-primary);
  }

  .is-featured .title {
    font-family: var(--font-heading);
    font-size: clamp(1rem, 2.2vw, 1.4rem);
    font-weight: 600;
    white-space: normal;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    line-clamp: 2;
    -webkit-box-orient: vertical;
  }
</style>
