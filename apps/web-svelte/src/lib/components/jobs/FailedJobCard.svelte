<script lang="ts">
  import { ChevronDown, ChevronUp, EyeOff } from "@lucide/svelte";
  import { Badge, StatusLed } from "@prismedia/ui-svelte";
  import type { JobRun } from "$lib/jobs/models";
  import {
    displayDescribeTrigger,
    displayJobHeading,
    errorFingerprint,
    formatRelativeTime,
    formatStamp,
    jobBadgeVariant,
    maintenanceJobLogRedacted,
  } from "$lib/jobs/helpers";
  import ForceRebuildBadge from "./ForceRebuildBadge.svelte";

  interface Props {
    job: JobRun;
    nsfwMode: string;
    occurrenceCount?: number;
    fingerprint?: string;
    onDismiss: (fingerprint: string) => void;
  }

  let {
    job,
    nsfwMode,
    occurrenceCount = 1,
    fingerprint = errorFingerprint(job),
    onDismiss,
  }: Props = $props();

  let open = $state(true);

  function handleDismiss(e: MouseEvent) {
    e.stopPropagation();
    onDismiss(fingerprint);
  }
</script>

<div class="surface-card no-lift border-status-error/25 p-4">
  <div class="flex flex-wrap items-start justify-between gap-3">
    <div class="min-w-0 flex-1">
      <div class="flex flex-wrap items-center gap-2">
        <StatusLed status="error" pulse={false} />
        <Badge variant={jobBadgeVariant(job)} class="text-[0.56rem]">
          {#snippet children()}{job.queueLabel}{/snippet}
        </Badge>
        <ForceRebuildBadge {job} />
        <span class="text-[0.62rem] font-semibold uppercase tracking-[0.12em] text-status-error-text">
          failed
        </span>
        {#if occurrenceCount > 1}
          <span class="rounded-xs border border-status-error/25 bg-status-error/[0.07] px-1.5 py-0.5 text-[0.56rem] font-semibold uppercase tracking-[0.12em] text-status-error-text">
            {occurrenceCount} occurrences
          </span>
        {/if}
      </div>
      <h3 class="mt-2 text-[0.95rem] font-medium text-text-primary">
        {displayJobHeading(job, nsfwMode)}
      </h3>
      <p class="mt-1 text-[0.74rem] text-text-muted">
        {displayDescribeTrigger(job, nsfwMode)}
      </p>
    </div>
    <div class="flex shrink-0 flex-col items-end gap-1">
      <p class="text-ephemeral">{formatRelativeTime(job.finishedAt ?? job.updatedAt)}</p>
      <p class="text-mono-sm text-text-disabled">attempt {Math.max(1, job.attempts + 1)}</p>
      <div class="mt-1 flex items-center gap-1">
        <button
          type="button"
          onclick={handleDismiss}
          class="flex items-center gap-1 px-1.5 py-0.5 text-[0.65rem] text-text-disabled transition-colors hover:text-text-muted"
          title="Suppress this error type"
        >
          <EyeOff class="h-3 w-3" />
          Suppress
        </button>
        <button
          type="button"
          onclick={() => (open = !open)}
          class="p-0.5 text-text-disabled transition-colors hover:text-text-muted"
          title={open ? "Collapse" : "Expand"}
        >
          {#if open}
            <ChevronUp class="h-3.5 w-3.5" />
          {:else}
            <ChevronDown class="h-3.5 w-3.5" />
          {/if}
        </button>
      </div>
    </div>
  </div>

  {#if open}
    <div class="mt-4 border-t border-border-subtle pt-4">
      <div class="grid gap-2 text-[0.7rem] text-text-disabled md:grid-cols-3">
        <div>
          <span class="text-text-muted">Queued:</span>
          {formatStamp(job.createdAt)}
        </div>
        <div>
          <span class="text-text-muted">Finished:</span>
          {formatStamp(job.finishedAt)}
        </div>
        <div>
          <span class="text-text-muted">Trigger:</span>
          {job.triggeredBy ?? "unknown"}
        </div>
      </div>
      <div class="mt-3 border border-status-error/20 bg-status-error/[0.05] p-3">
        <p class="mb-1 text-[0.68rem] uppercase tracking-[0.12em] text-status-error-text">
          Error output
        </p>
        <pre
          class="whitespace-pre-wrap break-words font-mono text-[0.75rem] leading-5 text-status-error-text">{maintenanceJobLogRedacted(
            job,
            nsfwMode,
          ) && job.error
            ? "Error details are hidden."
            : (job.error ?? "No error message recorded.")}</pre>
      </div>
    </div>
  {/if}
</div>
