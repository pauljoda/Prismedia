<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import { bandGradient, type SpectrumBand } from "$lib/entities/media-families";

  interface Props {
    bands: SpectrumBand[];
    /** Strip thickness in pixels. */
    height?: number;
    /** Show each band's label (and weight, when weighted) under the strip. */
    legend?: boolean;
    /** Accessible summary prefix, e.g. "Media in this library". */
    label?: string;
    class?: string;
  }

  let { bands, height = 6, legend = false, label = "Media families", class: className }: Props = $props();

  const weighted = $derived(bands.some((band) => band.weight !== undefined));
  const summary = $derived(
    bands.length === 0
      ? `${label}: none`
      : `${label}: ${bands.map((band) => (weighted ? `${band.label} ${band.weight ?? 0}` : band.label)).join(", ")}`,
  );
</script>

<!-- A segmented line of flat spectrum paint. Colour names the family; width carries any weight. -->
<div class={cn("flex min-w-0 flex-col gap-2", className)}>
  <div class="flex w-full gap-[2px]" style:height="{height}px" role="img" aria-label={summary}>
    {#each bands as band (band.key)}
      <span
        class="block h-full min-w-[6px] rounded-[2px]"
        style:flex-grow={band.weight ?? 1}
        style:flex-basis="0"
        style:background={bandGradient(band.accent)}
      ></span>
    {:else}
      <span class="block h-full w-full rounded-[2px] bg-[var(--color-surface-3)]"></span>
    {/each}
  </div>
  {#if legend && bands.length > 0}
    <ul class="flex flex-wrap gap-x-3.5 gap-y-1" aria-hidden="true">
      {#each bands as band (band.key)}
        <li class="flex items-center gap-1.5 text-caption text-text-muted">
          <span class="h-1.5 w-1.5 rounded-[1px]" style:background={band.accent.primary}></span>
          {band.label}
          {#if weighted}<span class="font-mono text-text-secondary">{band.weight}</span>{/if}
        </li>
      {/each}
    </ul>
  {/if}
</div>
