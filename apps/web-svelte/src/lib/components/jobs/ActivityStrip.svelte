<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import type { ActivityBucket } from "$lib/jobs/job-activity";

  interface Props {
    buckets: ActivityBucket[];
    /** Strip height in pixels. */
    height?: number;
    /** Print the −24h / −12h / now ruler under the strip. */
    axis?: boolean;
    /** What the strip counts, for the accessible summary. */
    label?: string;
    class?: string;
  }

  let { buckets, height = 24, axis = false, label = "Runs", class: className }: Props = $props();

  const peak = $derived(Math.max(1, ...buckets.map((bucket) => bucket.total)));
  const total = $derived(buckets.reduce((sum, bucket) => sum + bucket.total, 0));
  const failed = $derived(buckets.reduce((sum, bucket) => sum + bucket.failed, 0));
  const hourFormat = new Intl.DateTimeFormat(undefined, { hour: "numeric" });

  /** Bars keep a visible minimum so a single run in a busy lane is still a mark, not a gap. */
  function barHeight(bucket: ActivityBucket): number {
    return bucket.total === 0 ? 0 : Math.max(14, (bucket.total / peak) * 100);
  }

  function bucketTitle(bucket: ActivityBucket): string {
    const parts = [`${hourFormat.format(bucket.start)}`, `${bucket.total} run${bucket.total === 1 ? "" : "s"}`];
    if (bucket.failed > 0) parts.push(`${bucket.failed} failed`);
    if (bucket.running > 0) parts.push(`${bucket.running} running`);
    return parts.join(" · ");
  }
</script>

<!--
  Twenty-four hourly columns. Height carries the count (scaled to this strip's own peak); a failed
  share sits on top in the error hue so it never depends on colour alone — the lane's counts repeat it.
-->
<div class={cn("flex min-w-0 flex-col gap-1", className)}>
  <div
    class="flex w-full items-end gap-[2px]"
    style:height="{height}px"
    role="img"
    aria-label="{label}, last 24 hours: {total} total{failed > 0 ? `, ${failed} failed` : ''}, peak {peak} per hour"
  >
    {#each buckets as bucket (bucket.start)}
      <span class="flex h-full min-w-0 flex-1 flex-col justify-end" title={bucketTitle(bucket)}>
        {#if bucket.total === 0}
          <span class="block h-px w-full bg-[var(--color-border-default)]"></span>
        {:else}
          <span class="flex w-full flex-col overflow-hidden rounded-t-[2px]" style:height="{barHeight(bucket)}%">
            {#if bucket.failed > 0}
              <span class="block w-full bg-[var(--color-error)]" style:flex-grow={bucket.failed}></span>
            {/if}
            {#if bucket.running > 0}
              <span class="block w-full bg-[var(--color-text-primary)]" style:flex-grow={bucket.running}></span>
            {/if}
            {#if bucket.total - bucket.failed - bucket.running > 0}
              <span
                class="block w-full bg-[color-mix(in_oklab,var(--color-text-muted)_62%,transparent)]"
                style:flex-grow={bucket.total - bucket.failed - bucket.running}
              ></span>
            {/if}
          </span>
        {/if}
      </span>
    {/each}
  </div>
  {#if axis}
    <div class="flex justify-between font-mono text-[0.62rem] leading-none text-text-disabled" aria-hidden="true">
      <span>−24h</span>
      <span>−12h</span>
      <span>now</span>
    </div>
  {/if}
</div>
