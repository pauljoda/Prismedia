<script lang="ts">
  import { Button } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { toAspectRatioNumeric } from "$lib/entities/entity-thumbnail";
  import type { LatestItem } from "./dashboard-data";

  interface Props {
    items: LatestItem[];
    /** Items shown before "Show more". */
    pageSize?: number;
  }

  let { items, pageSize = 24 }: Props = $props();

  /** Narrowest column, in pixels. */
  const MIN_COLUMN = 168;
  const GAP = 16;
  /** Approximate caption height under the artwork (title, subtitle, chips), used only for balancing. */
  const CAPTION = 72;

  let width = $state(0);
  let shown = $state(0);
  const limit = $derived(shown || pageSize);
  const columnCount = $derived(Math.min(7, Math.max(2, Math.floor((width + GAP) / (MIN_COLUMN + GAP)))));
  const columnWidth = $derived(width > 0 ? (width - (columnCount - 1) * GAP) / columnCount : MIN_COLUMN);
  const visible = $derived(items.slice(0, limit));

  /**
   * Places each item in the currently shortest column, so the reading order stays roughly
   * left-to-right and newest-first while columns end at similar heights.
   */
  const columns = $derived.by(() => {
    const stacks: LatestItem[][] = Array.from({ length: columnCount }, () => []);
    const heights = Array.from({ length: columnCount }, () => 0);
    for (const item of visible) {
      const target = heights.indexOf(Math.min(...heights));
      stacks[target].push(item);
      heights[target] += columnWidth / toAspectRatioNumeric(item.card.aspectRatio) + CAPTION + GAP;
    }
    return stacks;
  });
</script>

<div class="flex flex-col gap-6">
  <div
    class="grid items-start"
    bind:clientWidth={width}
    style:grid-template-columns="repeat({columnCount}, minmax(0, 1fr))"
    style:column-gap="{GAP}px"
  >
    {#each columns as stack, index (index)}
      <div class="flex min-w-0 flex-col" style:gap="{GAP}px">
        {#each stack as item (`${item.card.entity.kind}:${item.card.entity.id}`)}
          <EntityThumbnail card={item.card} artworkReactive={false} />
        {/each}
      </div>
    {/each}
  </div>

  {#if items.length > limit}
    <div class="flex justify-center">
      <Button variant="outline" onclick={() => (shown = limit + pageSize)}>
        Show more
        <span class="font-mono text-caption tabular-nums text-text-muted">{items.length - limit}</span>
      </Button>
    </div>
  {/if}
</div>
