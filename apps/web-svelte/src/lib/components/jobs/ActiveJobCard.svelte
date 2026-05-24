<script lang="ts">
  import { Square } from "@lucide/svelte";
  import { StatusLed, cn } from "@prismedia/ui-svelte";
  import type { JobRun } from "$lib/jobs/models";
  import {
    displayJobDetail,
    displayJobHeading,
    displayJobKind,
    formatTargetDetail,
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
  const targetDetail = $derived(formatTargetDetail(job));
</script>

<div
  class={cn(
    "group border-b border-border-subtle/40 last:border-0",
    forceRebuild && "bg-status-error/[0.03]",
  )}
>
  <div class="flex items-start gap-3 px-3 py-2.5">
    <StatusLed status={toneForJob(job)} pulse={isRunning} />

    <div class="min-w-0 flex-1">
      <p class="truncate text-[0.82rem] font-medium text-text-primary">
        {displayJobHeading(job, nsfwMode)}
      </p>
      <div class="mt-1 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5 text-[0.68rem] leading-snug text-text-disabled">
        <span class="font-mono uppercase tracking-[0.08em] text-text-muted">
          {displayJobKind(job, nsfwMode)}
        </span>
        {#if targetDetail}
          <span class="min-w-0 truncate">{targetDetail}</span>
        {/if}
        <span class="min-w-0 truncate text-text-muted">{displayJobDetail(job, nsfwMode)}</span>
      </div>
    </div>

    {#if isRunning}
      <span class="w-8 text-right text-mono-sm tabular-nums text-text-accent"
        >{job.progress}%</span
      >
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

  {#if isRunning}
    <div class="meter-track mx-3 mb-2" style="height: 3px;">
      <div
        class={cn(forceRebuild ? "" : "meter-fill")}
        style="width: {job.progress}%;{forceRebuild ? ' background: var(--color-status-error); box-shadow: 0 0 6px rgba(168, 72, 80, 0.4);' : ''}"
      ></div>
    </div>
  {/if}
</div>
