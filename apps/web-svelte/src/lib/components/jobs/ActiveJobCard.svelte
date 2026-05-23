<script lang="ts">
  import { Square } from "@lucide/svelte";
  import { StatusLed, cn } from "@prismedia/ui-svelte";
  import type { JobRun } from "$lib/jobs/models";
  import {
    displayJobHeading,
    formatElapsed,
    isForceRebuildJob,
    statusLabel,
    toneForJob,
  } from "$lib/jobs/helpers";

  interface Props {
    job: JobRun;
    nsfwMode: string;
    cancellingJobRunId: string | null;
    onCancelJob: (job: JobRun) => void | Promise<void>;
  }

  let { job, nsfwMode, cancellingJobRunId, onCancelJob }: Props = $props();

  const isRunning = $derived(job.status === "active");
  const isCancelling = $derived(cancellingJobRunId === job.id);
  const forceRebuild = $derived(isForceRebuildJob(job));
</script>

<div
  class={cn(
    "flex items-center gap-3 border-b border-border-subtle/40 px-3 py-2 last:border-0",
    forceRebuild && "bg-status-error/[0.03]",
  )}
>
  <StatusLed status={toneForJob(job)} pulse={isRunning} />

  <div class="min-w-0 flex-1">
    <p class="truncate text-[0.82rem] font-medium text-text-primary">
      {displayJobHeading(job, nsfwMode)}
    </p>
  </div>

  {#if isRunning}
    <div class="flex shrink-0 items-center gap-1.5">
      <div class="h-1 w-20 overflow-hidden bg-surface-3">
        <div
          class={cn("h-full transition-all duration-500", forceRebuild ? "bg-status-error" : "bg-accent-500")}
          style="width: {job.progress}%"
        ></div>
      </div>
      <span class="w-7 text-right text-[0.65rem] tabular-nums text-text-disabled"
        >{job.progress}%</span
      >
    </div>
  {:else}
    <span
      class={cn(
        "shrink-0 text-[0.68rem] font-semibold uppercase tracking-[0.1em]",
        job.status === "delayed" ? "text-text-disabled" : "text-status-warning-text",
      )}
    >
      {statusLabel(job.status)}
    </span>
  {/if}

  <span class="w-20 shrink-0 text-right text-[0.7rem] tabular-nums text-text-muted">
    {formatElapsed(job)}
  </span>

  <button
    type="button"
    onclick={() => void onCancelJob(job)}
    disabled={isCancelling}
    class="shrink-0 text-text-disabled transition-colors hover:text-status-error-text disabled:opacity-30"
    title="Kill task"
  >
    <Square class="h-3 w-3" />
  </button>
</div>
