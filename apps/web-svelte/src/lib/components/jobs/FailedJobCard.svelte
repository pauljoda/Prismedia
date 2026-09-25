<script lang="ts">
  import { ChevronDown, EyeOff } from "@lucide/svelte";
  import { Button, StatusLed } from "@prismedia/ui-svelte";
  import type { JobRun } from "$lib/jobs/models";
  import {
    displayDescribeTrigger,
    displayJobHeading,
    errorFingerprint,
    formatRelativeTime,
    formatStamp,
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

  let open = $state(false);

  const errorText = $derived(
    maintenanceJobLogRedacted(job, nsfwMode) && job.error
      ? "Error details are hidden."
      : (job.error ?? "No error message recorded."),
  );

  function handleDismiss(e: MouseEvent) {
    e.stopPropagation();
    onDismiss(fingerprint);
  }
</script>

<article
  class="min-w-0 overflow-hidden rounded-[var(--radius-md)] border border-[color-mix(in_oklab,var(--color-error)_28%,transparent)] bg-[var(--color-surface-2)]"
  aria-label={displayJobHeading(job, nsfwMode)}
>
  <div class="flex min-w-0 items-start gap-3 px-4 py-3">
    <StatusLed status="error" size="sm" class="mt-1.5" />
    <div class="min-w-0 flex-1">
      <div class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
        <span class="font-mono text-[0.64rem] uppercase tracking-[0.12em] text-text-muted">{job.queueLabel}</span>
        <ForceRebuildBadge {job} />
        {#if occurrenceCount > 1}
          <span class="font-mono text-[0.64rem] text-error-text">{occurrenceCount} occurrences</span>
        {/if}
      </div>
      <h3 class="truncate text-label font-medium text-text-primary">{displayJobHeading(job, nsfwMode)}</h3>
      {#if !open}
        <p class="truncate text-caption text-error-text">{errorText.split("\n")[0]}</p>
      {/if}
    </div>
    <div class="flex shrink-0 items-center gap-1">
      <span class="hidden font-mono text-[0.64rem] text-text-disabled sm:inline">
        {formatRelativeTime(job.finishedAt ?? job.updatedAt)}
      </span>
      <Button variant="ghost" size="sm" onclick={handleDismiss} title="Suppress this error type" aria-label="Suppress">
        <EyeOff aria-hidden="true" />
        <span class="hidden sm:inline">Suppress</span>
      </Button>
      <Button
        variant="ghost"
        size="icon-sm"
        onclick={() => (open = !open)}
        aria-expanded={open}
        aria-label={open ? "Hide details" : "Show details"}
      >
        <ChevronDown class={open ? "rotate-180" : undefined} aria-hidden="true" />
      </Button>
    </div>
  </div>

  {#if open}
    <div class="flex flex-col gap-3 border-t border-[var(--color-border-subtle)] px-4 py-3">
      <dl class="grid gap-x-4 gap-y-1 font-mono text-[0.66rem] text-text-disabled sm:grid-cols-4">
        <div><dt class="inline text-text-muted">Queued </dt><dd class="inline">{formatStamp(job.createdAt)}</dd></div>
        <div><dt class="inline text-text-muted">Finished </dt><dd class="inline">{formatStamp(job.finishedAt)}</dd></div>
        <div><dt class="inline text-text-muted">Trigger </dt><dd class="inline">{displayDescribeTrigger(job, nsfwMode)}</dd></div>
        <div><dt class="inline text-text-muted">Attempt </dt><dd class="inline">{Math.max(1, job.attempts + 1)}</dd></div>
      </dl>
      <pre
        class="whitespace-pre-wrap break-words rounded-[var(--radius-sm)] bg-[var(--color-surface-1)] p-3 font-mono text-[0.72rem] leading-5 text-error-text">{errorText}</pre>
    </div>
  {/if}
</article>
