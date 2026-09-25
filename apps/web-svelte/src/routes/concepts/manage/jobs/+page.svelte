<script lang="ts">
  import { onMount } from "svelte";
  import { prefersReducedMotion } from "svelte/motion";
  import { Activity, RefreshCw, ShieldUser, SquareArrowOutUpRight } from "@lucide/svelte";
  import { Alert, Button, Progress, StatusLed, buttonVariants } from "@prismedia/ui-svelte";
  import { JOB_GRAPH_STATUS } from "$lib/api/generated/codes";
  import type { JobGraphSummary, JobQueueCountDto, JobRun } from "$lib/api/generated/model";
  import { createJob, fetchJobGraphs, fetchJobs, fetchWorkerHealth } from "$lib/api/jobs";
  import { createSerializedRefresh } from "$lib/async/serialized-refresh";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import PrismPageHeader from "$lib/components/concepts/PrismPageHeader.svelte";
  import JobCommandBar from "$lib/components/concepts/manage/JobCommandBar.svelte";
  import JobLaneRow from "$lib/components/concepts/manage/JobLaneRow.svelte";
  import QuietLaneList from "$lib/components/concepts/manage/QuietLaneList.svelte";
  import ManageConceptFrame from "$lib/components/concepts/manage/ManageConceptFrame.svelte";
  import { jobRunMoment } from "$lib/components/concepts/manage/job-activity";
  import {
    JOB_LANE_SECTIONS,
    SCAN_JOB_TYPES,
    buildJobLanes,
    isQuietLane,
    totalLaneCounts,
  } from "$lib/components/concepts/manage/job-lanes";
  import { groupJobGraphsByActivity, jobLabelForType } from "$lib/jobs/jobs-dashboard";
  import { RUN_CATALOG } from "$lib/jobs/run-catalog";
  import { describeWorkerHealth, type WorkerHealthBadge } from "$lib/jobs/worker-health";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useSession } from "$lib/stores/session.svelte";
  import { formatRelativeTime } from "$lib/utils/format";

  const session = useSession();
  const nsfw = useNsfw();
  const POLL_MS = 8_000;

  let runs = $state.raw<JobRun[]>([]);
  let counts = $state.raw<JobQueueCountDto[]>([]);
  let graphs = $state.raw<JobGraphSummary[]>([]);
  let worker = $state<WorkerHealthBadge>(describeWorkerHealth(null));
  let loaded = $state(false);
  let refreshing = $state(false);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);
  let pendingType = $state<string | null>(null);
  let now = $state(Date.now());

  async function load() {
    refreshing = true;
    const hideNsfw = nsfw.mode === "off";
    const [jobResult, graphResult, healthResult] = await Promise.allSettled([
      fetchJobs(hideNsfw),
      fetchJobGraphs(hideNsfw),
      fetchWorkerHealth(),
    ]);
    if (jobResult.status === "fulfilled") {
      runs = jobResult.value.items;
      counts = jobResult.value.counts;
      error = null;
    } else {
      error = jobResult.reason instanceof Error ? jobResult.reason.message : "Could not load job lanes";
    }
    if (graphResult.status === "fulfilled") graphs = graphResult.value.items;
    worker = describeWorkerHealth(
      healthResult.status === "fulfilled"
        ? healthResult.value
        : { status: "offline", workerId: null, lastSeenAt: null, staleAfterSeconds: 45 },
    );
    now = Date.now();
    loaded = true;
    refreshing = false;
  }

  const refresh = createSerializedRefresh(load);

  onMount(() => {
    if (!session.isAdmin) return;
    void refresh();
    const timer = setInterval(() => void refresh(), POLL_MS);
    return () => clearInterval(timer);
  });

  let lastNsfwMode = nsfw.mode;
  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void refresh();
  });

  async function run(jobType: string) {
    pendingType = jobType;
    message = null;
    const entry = RUN_CATALOG.flatMap((group) => group.entries).find((candidate) => candidate.jobType === jobType);
    try {
      await createJob(jobType);
      message = `Queued · ${entry?.label ?? jobLabelForType(jobType)}`;
      await refresh();
    } catch (cause) {
      error = cause instanceof Error ? cause.message : "Could not queue that job";
    } finally {
      pendingType = null;
    }
  }

  const motionOk = $derived(!prefersReducedMotion.current);
  const lanes = $derived(buildJobLanes(runs, counts, now));
  const totals = $derived(totalLaneCounts(lanes));
  const sections = $derived(
    JOB_LANE_SECTIONS.map((section) => {
      const sectionLanes = lanes.filter((lane) => lane.section === section.id);
      return {
        ...section,
        total: sectionLanes.length,
        live: sectionLanes.filter((lane) => !isQuietLane(lane)),
        quiet: sectionLanes.filter(isQuietLane),
      };
    }).filter((section) => section.total > 0),
  );
  const graphGroups = $derived(groupJobGraphsByActivity(graphs));
  const liveGraphs = $derived([...graphGroups.active, ...graphGroups.waiting]);
  const lastScanAt = $derived.by(() => {
    const scans = runs.filter((candidate) => SCAN_JOB_TYPES.has(candidate.type) && candidate.finishedAt);
    let latest: string | null = null;
    for (const scan of scans) {
      const moment = jobRunMoment(scan);
      if (!latest || Date.parse(moment) > Date.parse(latest)) latest = moment;
    }
    return latest;
  });
  const compact = new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 1 });
</script>

<svelte:head><title>Job lanes · Concepts · Prismedia</title></svelte:head>

<ManageConceptFrame concept="Job lanes">
  {#if !session.isAdmin}
    <StatePlaceholder icon={ShieldUser} title="Administrator access required" />
  {:else}
    <PrismPageHeader icon={Activity} title="Job control">
      {#snippet status()}
        <span class="flex items-center gap-2 text-caption text-text-secondary" title={worker.tooltip}>
          <StatusLed status={worker.led} size="sm" pulse={worker.pulse && motionOk} />
          {worker.label}
        </span>
        {#if loaded}
          <span class="flex flex-wrap gap-x-3 font-mono text-[0.72rem] text-text-muted">
            <span class={totals.running > 0 ? "text-text-primary" : undefined}>{totals.running} running</span>
            <span>{totals.queued} queued</span>
            <span class={totals.failed > 0 ? "text-error-text" : undefined}>{totals.failed} failed</span>
            <span>{compact.format(totals.completed)} done</span>
          </span>
          {#if lastScanAt}
            <span class="font-mono text-[0.72rem] text-text-muted">scan {formatRelativeTime(lastScanAt, true)}</span>
          {/if}
          <span class="font-mono text-[0.72rem] text-text-disabled">{runs.length} runs in view</span>
        {/if}
      {/snippet}
      {#snippet actions()}
        <Button variant="ghost" size="sm" onclick={() => void refresh()} disabled={refreshing}>
          <RefreshCw class={refreshing && motionOk ? "animate-spin" : undefined} aria-hidden="true" />
          Refresh
        </Button>
        <a href="/jobs" class={buttonVariants({ variant: "outline", size: "sm" })}>
          <SquareArrowOutUpRight aria-hidden="true" />
          Job control
        </a>
      {/snippet}
    </PrismPageHeader>

    <JobCommandBar onRun={(jobType) => void run(jobType)} {pendingType} />

    {#if error}
      <Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>
    {:else if message}
      <Alert.Root><Alert.Description>{message}</Alert.Description></Alert.Root>
    {/if}

    {#if liveGraphs.length > 0}
      <section class="flex flex-col gap-2" aria-labelledby="jobs-now">
        <h2 id="jobs-now" class="font-heading text-base font-semibold text-text-primary">
          Now <span class="ml-1 font-mono text-caption font-normal text-text-muted">{liveGraphs.length}</span>
        </h2>
        <ul class="grid gap-2 md:grid-cols-2">
          {#each liveGraphs as graph (graph.id)}
            {@const waiting = graph.status === JOB_GRAPH_STATUS.waiting}
            {@const progress = Number(graph.progress) || 0}
            <li
              class="flex min-w-0 items-start gap-3 rounded-[var(--radius-md)] border border-[var(--color-border-default)] bg-[var(--color-surface-2)] px-4 py-3"
            >
              <StatusLed
                status={waiting ? "warning" : graph.status === JOB_GRAPH_STATUS.running ? "phosphor" : "info"}
                size="sm"
                pulse={graph.status === JOB_GRAPH_STATUS.running && motionOk}
                class="mt-1.5"
              />
              <div class="min-w-0 flex-1">
                <p class="truncate text-label font-medium text-text-primary">{graph.displayName}</p>
                <p class="truncate text-caption text-text-muted">
                  {waiting ? (graph.waitReason ?? "Waiting on review") : jobLabelForType(graph.currentNodeType, graph.status)}
                  · {graph.completedNodeCount}/{graph.nodeCount} steps
                </p>
                <Progress value={progress} aria-label="{graph.displayName} progress" class="mt-2 h-1.5 rounded-xs" />
              </div>
              <span class="shrink-0 font-mono text-caption text-text-secondary tabular-nums">{Math.round(progress)}%</span>
            </li>
          {/each}
        </ul>
      </section>
    {/if}

    {#if !loaded}
      <StatePlaceholder icon={Activity} title="Reading job lanes" busy />
    {:else if lanes.length === 0}
      <StatePlaceholder icon={Activity} title="No background work" />
    {:else}
      <div class="flex flex-wrap items-center justify-end gap-x-4 gap-y-2 text-caption text-text-muted">
        <span class="font-mono text-[0.68rem] text-text-disabled">24h · per hour</span>
        <ul class="flex flex-wrap gap-x-4 gap-y-1" aria-label="Strip key">
          <li class="flex items-center gap-1.5">
            <span class="h-2.5 w-1.5 rounded-[1px] bg-[color-mix(in_oklab,var(--color-text-muted)_62%,transparent)]"></span>
            Runs
          </li>
          <li class="flex items-center gap-1.5">
            <span class="h-2.5 w-1.5 rounded-[1px] bg-[var(--color-text-primary)]"></span>
            Running
          </li>
          <li class="flex items-center gap-1.5">
            <span class="h-2.5 w-1.5 rounded-[1px] bg-[var(--color-error)]"></span>
            Failed
          </li>
        </ul>
      </div>

      {#each sections as section (section.id)}
        <section class="flex flex-col gap-2" aria-labelledby="lane-section-{section.id}">
          <h2 id="lane-section-{section.id}" class="font-heading text-base font-semibold text-text-primary">
            {section.title}
            <span class="ml-1 font-mono text-caption font-normal text-text-muted">{section.total}</span>
          </h2>
          <div class="flex flex-col gap-2">
            {#each section.live as lane (lane.type)}
              <JobLaneRow {lane} nsfwMode={nsfw.mode} />
            {/each}
            {#if section.quiet.length > 0}
              <QuietLaneList lanes={section.quiet} />
            {/if}
          </div>
        </section>
      {/each}

    {/if}
  {/if}
</ManageConceptFrame>
