<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import type { JobLane } from "$lib/jobs/job-lanes";

  interface Props {
    lanes: JobLane[];
    class?: string;
  }

  let { lanes, class: className }: Props = $props();
  const compact = new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 1 });
</script>

<!--
  Lanes with nothing in the recent window fold into one quiet list: still listed, still counted,
  but they do not spend a full strip saying "nothing happened".
-->
<div
  class={cn(
    "rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)]",
    className,
  )}
>
  <p class="px-4 pt-3 pb-1 font-mono text-[0.6rem] uppercase tracking-[0.14em] text-text-disabled">Quiet</p>
  <ul class="divide-y divide-[var(--color-border-subtle)]">
    {#each lanes as lane (lane.type)}
      <li class="flex min-w-0 items-baseline justify-between gap-4 px-4 py-2">
        <span class="min-w-0 truncate text-label text-text-secondary">{lane.label}</span>
        <span class="flex shrink-0 gap-3 font-mono text-[0.7rem] tabular-nums text-text-muted">
          {#if lane.counts.failed > 0}<span class="text-error-text">{compact.format(lane.counts.failed)} failed</span>{/if}
          <span>{compact.format(lane.counts.completed)} done</span>
        </span>
      </li>
    {/each}
  </ul>
</div>
