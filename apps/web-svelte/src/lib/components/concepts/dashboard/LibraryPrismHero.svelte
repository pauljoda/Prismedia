<script lang="ts">
  import type { Snippet } from "svelte";
  import { resolve } from "$app/paths";
  import { FolderPlus } from "@lucide/svelte";
  import { Skeleton, buttonVariants, cn } from "@prismedia/ui-svelte";
  import PrismDispersion from "$lib/components/stats/PrismDispersion.svelte";
  import { compositionBands } from "./dashboard-data";
  import { libraryTotal, type LibraryFamily } from "../library-composition";

  interface Props {
    /** Library composition in spectrum order; empty while it loads. */
    families: LibraryFamily[];
    /** Family whose newest items are open beneath the prism. */
    activeKind: string | null;
    onSelect: (kind: string | null) => void;
    /** Content revealed under the prism for the selected family. */
    focus?: Snippet;
  }

  let { families, activeKind, onSelect, focus }: Props = $props();

  const bands = $derived(compositionBands(families));
  const total = $derived(libraryTotal(families));
  const numberFormat = new Intl.NumberFormat();
</script>

<!-- The prism and its legend describe the library on their own; only the totals sit above them. -->
<section aria-label="Library composition" class="flex flex-col gap-3">
  {#if families.length === 0}
    <Skeleton class="h-8 w-40" />
    <div class="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(15rem,19rem)]" aria-hidden="true">
      <Skeleton class="h-[150px] rounded-[var(--radius-sm)] sm:h-[220px]" />
      <div class="flex flex-col gap-1.5">
        {#each Array(6) as _, index (index)}
          <Skeleton class="h-9 rounded-[var(--radius-xs)]" />
        {/each}
      </div>
    </div>
  {:else if bands.length === 0}
    <div class="flex flex-col items-start gap-3 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] p-6">
      <p class="font-heading text-lg font-semibold text-text-primary">No media</p>
      <a href={resolve("/settings/libraries")} class={cn(buttonVariants({ variant: "outline", size: "sm" }), "gap-2")}>
        <FolderPlus class="h-4 w-4" aria-hidden="true" />
        Add a library
      </a>
    </div>
  {:else}
    <p class="flex flex-wrap items-baseline gap-x-4 gap-y-1 px-1">
      <span class="flex items-baseline gap-2">
        <span class="font-heading text-3xl font-semibold tabular-nums tracking-tight text-text-primary">{numberFormat.format(total)}</span>
        <span class="text-sm text-text-muted">items</span>
      </span>
      <span class="font-mono text-caption tabular-nums text-text-muted">{bands.length} families</span>
    </p>
    <PrismDispersion {bands} {activeKind} {onSelect} compactLegend>
      {#snippet detail(band)}
        {numberFormat.format(band.distinctEntityCount)} {band.distinctEntityCount === 1 ? "item" : "items"}
      {/snippet}
    </PrismDispersion>
  {/if}

  {#if focus}
    {@render focus()}
  {/if}
</section>
