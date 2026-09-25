<script lang="ts">
  import { Loader2 } from "@lucide/svelte";
  import { prefersReducedMotion } from "svelte/motion";
  import { Button, cn } from "@prismedia/ui-svelte";
  import { RUN_CATALOG } from "$lib/jobs/run-catalog";

  interface Props {
    /** Queues one administrative job type. */
    onRun: (jobType: string) => void;
    /** The job type currently being queued, if any. */
    pendingType: string | null;
    class?: string;
  }

  let { onRun, pendingType, class: className }: Props = $props();
  const motionOk = $derived(!prefersReducedMotion.current);
</script>

<!--
  The administrative run catalog as one compact bar: each group a labelled cluster of the same
  actions Job control offers, so starting a scan never means scrolling past history.
-->
<div
  class={cn(
    "flex flex-col gap-3 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] p-3 sm:flex-row sm:flex-wrap sm:items-center sm:gap-x-6",
    className,
  )}
  role="toolbar"
  aria-label="Start administrative work"
>
  {#each RUN_CATALOG as group (group.id)}
    <div class="flex min-w-0 flex-col gap-1.5 sm:flex-row sm:items-center sm:gap-2.5">
      <span class="font-mono text-[0.62rem] uppercase tracking-[0.14em] text-text-disabled">{group.title}</span>
      <div class="flex flex-wrap gap-1.5">
        {#each group.entries as entry (entry.jobType)}
          {@const Icon = entry.icon}
          <Button
            variant="outline"
            size="sm"
            disabled={pendingType !== null}
            onclick={() => onRun(entry.jobType)}
          >
            {#if pendingType === entry.jobType}
              <Loader2 class={motionOk ? "animate-spin" : undefined} aria-hidden="true" />
            {:else}
              <Icon aria-hidden="true" />
            {/if}
            {entry.label}
          </Button>
        {/each}
      </div>
    </div>
  {/each}
</div>
