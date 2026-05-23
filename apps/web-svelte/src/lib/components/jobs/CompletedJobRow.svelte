<script lang="ts">
  import { Badge } from "@prismedia/ui-svelte";
  import type { JobRun } from "$lib/jobs/models";
  import {
    displayDescribeTrigger,
    displayJobHeading,
    formatDuration,
    formatRelativeTime,
    formatStamp,
    jobBadgeVariant,
  } from "$lib/jobs/helpers";
  import ForceRebuildBadge from "./ForceRebuildBadge.svelte";

  interface Props {
    job: JobRun;
    nsfwMode: string;
  }

  let { job, nsfwMode }: Props = $props();
</script>

<div class="grid gap-2 px-4 py-2.5 md:grid-cols-[1fr_auto_auto_auto]">
  <div class="min-w-0">
    <p class="truncate text-[0.82rem] font-medium text-text-primary">
      {displayJobHeading(job, nsfwMode)}
    </p>
    <p class="mt-0.5 truncate text-mono-sm text-text-disabled">
      {displayDescribeTrigger(job, nsfwMode)}
    </p>
  </div>
  <div class="flex items-center gap-2">
    <Badge variant={jobBadgeVariant(job)} class="text-[0.56rem]">
      {#snippet children()}{job.queueLabel}{/snippet}
    </Badge>
    <ForceRebuildBadge {job} />
    {#if job.attempts > 1}
      <span class="text-[0.6rem] text-status-warning-text" title="Succeeded after retries"
        >×{job.attempts}</span
      >
    {/if}
  </div>
  <div class="flex items-center text-[0.7rem] tabular-nums text-text-muted">
    {formatDuration(job)}
  </div>
  <div class="text-right text-[0.72rem] text-text-muted">
    <div>{formatRelativeTime(job.finishedAt ?? job.updatedAt)}</div>
    <div class="mt-0.5 text-text-disabled">{formatStamp(job.finishedAt)}</div>
  </div>
</div>
