<script lang="ts">
  import { ChevronDown } from "@lucide/svelte";
  import { Button, Collapsible, StatusLed, cn, type LedStatus } from "@prismedia/ui-svelte";
  import { JOB_RUN_STATUS } from "$lib/api/generated/codes";
  import type { JobRun } from "$lib/api/generated/model";
  import { displayJobDetail, displayJobHeading } from "$lib/jobs/helpers";
  import { mapJobRun } from "$lib/jobs/jobs-dashboard";
  import { formatRelativeTime } from "$lib/utils/format";
  import ActivityStrip from "./ActivityStrip.svelte";
  import { jobRunMoment } from "$lib/jobs/job-activity";
  import type { JobLane } from "$lib/jobs/job-lanes";

  interface Props {
    lane: JobLane;
    nsfwMode: string;
    /** How many recent runs the expanded list shows before offering the rest. */
    listLimit?: number;
  }

  let { lane, nsfwMode, listLimit = 12 }: Props = $props();

  let open = $state(false);
  let showAll = $state(false);
  const compact = new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 1 });

  const laneLed = $derived.by((): LedStatus => {
    if (lane.counts.running > 0) return "phosphor";
    if (lane.counts.failed > 0) return "error";
    if (lane.counts.queued > 0) return "info";
    return "idle";
  });
  const laneState = $derived(
    lane.counts.running > 0
      ? "Running"
      : lane.counts.failed > 0
        ? "Has failures"
        : lane.counts.queued > 0
          ? "Queued"
          : "Idle",
  );

  const stats = $derived([
    { label: "Running", value: lane.counts.running, tone: lane.counts.running > 0 ? "text-text-primary" : "text-text-disabled" },
    { label: "Queued", value: lane.counts.queued, tone: lane.counts.queued > 0 ? "text-text-primary" : "text-text-disabled" },
    { label: "Failed", value: lane.counts.failed, tone: lane.counts.failed > 0 ? "text-error-text" : "text-text-disabled" },
    { label: "Done", value: lane.counts.completed, tone: "text-text-secondary" },
  ]);

  const shownRuns = $derived(
    (showAll ? lane.runs : lane.runs.slice(0, listLimit)).map((run) => ({ run, view: mapJobRun(run) })),
  );

  const RUN_STATUS_LABEL: Record<string, string> = {
    [JOB_RUN_STATUS.running]: "Running",
    [JOB_RUN_STATUS.queued]: "Queued",
    [JOB_RUN_STATUS.completed]: "Done",
    [JOB_RUN_STATUS.failed]: "Failed",
    [JOB_RUN_STATUS.cancelled]: "Cancelled",
  };

  function runLed(run: JobRun): LedStatus {
    if (run.status === JOB_RUN_STATUS.running) return "phosphor";
    if (run.status === JOB_RUN_STATUS.failed) return "error";
    if (run.status === JOB_RUN_STATUS.queued) return "info";
    return "idle";
  }
</script>

<!-- One job type as a lane: its state, the last day as a strip, and retained totals. Runs fold inside. -->
<article
  class="min-w-0 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)]"
  aria-label="{lane.label}: {laneState}"
>
  <Collapsible.Root bind:open>
    <div
      class="grid min-w-0 gap-3 p-3.5 sm:p-4 lg:grid-cols-[minmax(0,15rem)_minmax(0,1fr)_minmax(0,14rem)] lg:items-center lg:gap-6"
    >
      <div class="flex min-w-0 items-center gap-2.5">
        <StatusLed status={laneLed} size="sm" />
        <h3 class="min-w-0 truncate font-heading text-sm font-semibold text-text-primary">{lane.label}</h3>
      </div>

      <ActivityStrip buckets={lane.buckets} height={28} label="{lane.label} runs" />

      <dl class="grid grid-cols-4 gap-2">
        {#each stats as stat (stat.label)}
          <div class="min-w-0 lg:text-right">
            <dt class="font-mono text-[0.58rem] uppercase tracking-[0.12em] text-text-disabled">{stat.label}</dt>
            <dd class={cn("font-mono text-sm tabular-nums", stat.tone)} title={String(stat.value)}>
              {compact.format(stat.value)}
            </dd>
          </div>
        {/each}
      </dl>
    </div>

    {#if lane.runs.length > 0}
      <Collapsible.Trigger
        class="flex w-full items-center justify-between gap-3 rounded-b-[var(--radius-md)] border-t border-[var(--color-border-subtle)] px-4 py-2.5 text-left text-caption text-text-muted transition-colors hover:bg-[var(--color-surface-3)]/40 hover:text-text-primary focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-[var(--color-border-accent-strong)] data-[state=open]:rounded-b-none"
      >
        <span>
          <span class="font-mono text-text-secondary">{lane.runs.length}</span>
          recent {lane.runs.length === 1 ? "run" : "runs"}
        </span>
        <span class="flex items-center gap-2">
          {#if lane.lastRunAt}
            <span class="font-mono text-[0.68rem] text-text-disabled">{formatRelativeTime(lane.lastRunAt)}</span>
          {/if}
          <ChevronDown class={cn("size-3.5 transition-transform motion-reduce:transition-none", open && "rotate-180")} aria-hidden="true" />
        </span>
      </Collapsible.Trigger>
      <Collapsible.Content>
        <ol class="divide-y divide-[var(--color-border-subtle)] border-t border-[var(--color-border-subtle)]">
          {#each shownRuns as { run, view } (run.id)}
            <li class="flex min-w-0 items-center gap-3 px-4 py-2">
              <StatusLed status={runLed(run)} size="sm" />
              <div class="min-w-0 flex-1">
                <p class="truncate text-label text-text-primary">{displayJobHeading(view, nsfwMode)}</p>
                <p
                  class={cn(
                    "truncate text-caption",
                    run.status === JOB_RUN_STATUS.failed ? "text-error-text" : "text-text-muted",
                  )}
                >
                  {displayJobDetail(view, nsfwMode)}
                </p>
              </div>
              <span class="shrink-0 text-right font-mono text-[0.64rem] leading-tight text-text-disabled">
                <span class="block uppercase tracking-[0.1em]">{RUN_STATUS_LABEL[run.status] ?? "Updating"}</span>
                {formatRelativeTime(jobRunMoment(run))}
              </span>
            </li>
          {/each}
        </ol>
        {#if lane.runs.length > listLimit && !showAll}
          <div class="border-t border-[var(--color-border-subtle)] px-2 py-1.5">
            <Button variant="ghost" size="sm" onclick={() => (showAll = true)}>
              Show {lane.runs.length - listLimit} more
            </Button>
          </div>
        {/if}
      </Collapsible.Content>
    {/if}
  </Collapsible.Root>
</article>
