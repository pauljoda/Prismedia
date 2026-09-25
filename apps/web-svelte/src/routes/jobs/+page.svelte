<script lang="ts">
  import { onDestroy, onMount } from "svelte";
  import { Activity, AlertTriangle, Ban, Loader2, RefreshCw } from "@lucide/svelte";
  import { Alert, Button, StatusLed, cn } from "@prismedia/ui-svelte";
  import {
    cancelJobGraph,
    clearJobFailures,
    createJob,
    fetchJobActivity,
    fetchJobGraph,
    fetchJobGraphs,
    fetchJobs,
    fetchWorkerHealth,
  } from "$lib/api/jobs";
  import type { JobActivityBucket, JobGraphDetailResponse, JobGraphSummary, JobQueueCountDto, JobRun } from "$lib/api/generated/model";
  import { JOB_RUN_STATUS } from "$lib/api/generated/codes";
  import { fetchSettingsValues } from "$lib/api/settings";
  import { settingKeys, valuesToLibrarySettings } from "$lib/settings/app-settings";
  import type { JobsDashboard } from "$lib/jobs/models";
  import { buildJobsDashboard, groupJobGraphsByActivity, jobLabelForType, type ScheduleInfo } from "$lib/jobs/jobs-dashboard";
  import { jobRunMoment } from "$lib/jobs/job-activity";
  import { JOB_LANE_SECTIONS, SCAN_JOB_TYPES, buildJobLanes, isQuietLane, totalLaneCounts } from "$lib/jobs/job-lanes";
  import { RUN_CATALOG } from "$lib/jobs/run-catalog";
  import { formatRelativeTimeShort, groupFailedJobs } from "$lib/jobs/helpers";
  import { describeWorkerHealth, type WorkerHealthBadge } from "$lib/jobs/worker-health";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { dismissedErrors } from "$lib/stores/dismissed-errors.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import ManagePageHeader from "$lib/components/manage/ManagePageHeader.svelte";
  import FailedJobCard from "$lib/components/jobs/FailedJobCard.svelte";
  import GraphLaneCard from "$lib/components/jobs/GraphLaneCard.svelte";
  import JobCommandBar from "$lib/components/jobs/JobCommandBar.svelte";
  import JobLaneRow from "$lib/components/jobs/JobLaneRow.svelte";
  import QuietLaneList from "$lib/components/jobs/QuietLaneList.svelte";

  const POLL_MS = 5_000;
  /** Failure groups listed before the rest fold behind a button. */
  const FAILURE_PREVIEW = 4;
  const nsfw = useNsfw();

  let runs = $state.raw<JobRun[]>([]);
  let counts = $state.raw<JobQueueCountDto[]>([]);
  let activity = $state.raw<JobActivityBucket[]>([]);
  let graphs = $state.raw<JobGraphSummary[]>([]);
  let graphDetails = $state.raw<Record<string, JobGraphDetailResponse>>({});
  let dashboard = $state.raw<JobsDashboard | null>(null);
  let scheduleInfo = $state<ScheduleInfo | undefined>(undefined);
  let workerHealth = $state<WorkerHealthBadge>(describeWorkerHealth(null));
  let loading = $state(true);
  /** Only a refresh the user asked for spins; background polling stays still. */
  let refreshing = $state(false);
  let now = $state(Date.now());
  let expandedGraphId = $state<string | null>(null);
  let loadingGraphId = $state<string | null>(null);
  let cancellingGraphId = $state<string | null>(null);
  let runningJobType = $state<string | null>(null);
  let clearingFailures = $state(false);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let lastNsfwMode = $state(nsfw.mode);
  let showAllFailures = $state(false);

  const graphGroups = $derived(groupJobGraphsByActivity(graphs));
  const liveGraphs = $derived([...graphGroups.active, ...graphGroups.waiting]);
  const waitingCount = $derived(graphGroups.waiting.length);
  const lanes = $derived(buildJobLanes(runs, counts, activity, now));
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
  const failedGroups = $derived(groupFailedJobs(dashboard?.failedJobs ?? []));
  const visibleFailedGroups = $derived(
    failedGroups.filter((group) => !dismissedErrors.isDismissed(group.fingerprint)),
  );
  const shownFailedGroups = $derived(
    showAllFailures ? visibleFailedGroups : visibleFailedGroups.slice(0, FAILURE_PREVIEW),
  );
  /** The most recent finished scan of any kind among the retained runs, when there is one. */
  const lastScanAt = $derived.by(() => {
    let latest: string | null = null;
    for (const candidate of runs) {
      if (!SCAN_JOB_TYPES.has(candidate.type) || candidate.status !== JOB_RUN_STATUS.completed) continue;
      const moment = jobRunMoment(candidate);
      if (!latest || Date.parse(moment) > Date.parse(latest)) latest = moment;
    }
    return latest;
  });
  const compact = new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 1 });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadDashboard();
  });

  async function refreshNow() {
    refreshing = true;
    await Promise.all([loadDashboard(), loadWorkerHealth()]);
    refreshing = false;
  }

  async function loadDashboard() {
    try {
      const hideNsfw = nsfw.mode === "off";
      const [graphResponse, jobResponse, activityResponse] = await Promise.all([
        fetchJobGraphs(hideNsfw),
        fetchJobs(hideNsfw),
        fetchJobActivity(hideNsfw),
      ]);
      graphs = graphResponse.items;
      runs = jobResponse.items;
      counts = jobResponse.counts;
      activity = activityResponse.buckets;
      dashboard = buildJobsDashboard(jobResponse.items, scheduleInfo, jobResponse.counts);
      // The strips align to the server's clock, which cut the hourly buckets.
      now = Date.parse(activityResponse.now) || Date.now();
      if (expandedGraphId) {
        const detail = await fetchJobGraph(expandedGraphId, hideNsfw);
        graphDetails = { ...graphDetails, [expandedGraphId]: detail };
      }
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load jobs";
    } finally {
      loading = false;
    }
  }

  async function loadSchedule() {
    try {
      const config = await fetchSettingsValues([settingKeys.scanAutoScanEnabled, settingKeys.scanIntervalMinutes]);
      const settings = valuesToLibrarySettings(config.values);
      scheduleInfo = { enabled: settings.autoScanEnabled, intervalMinutes: settings.scanIntervalMinutes };
    } catch {
      // The schedule is informational; job state remains authoritative.
    }
  }

  async function loadWorkerHealth() {
    try {
      workerHealth = describeWorkerHealth(await fetchWorkerHealth());
    } catch {
      workerHealth = describeWorkerHealth({ status: "offline", workerId: null, lastSeenAt: null, staleAfterSeconds: 45 });
    }
  }

  onMount(() => {
    dismissedErrors.init();
    void loadSchedule();
    void loadWorkerHealth();
    void loadDashboard();
    pollTimer = setInterval(() => {
      void loadDashboard();
      void loadWorkerHealth();
    }, POLL_MS);
  });

  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });

  async function handleRun(jobType: string) {
    runningJobType = jobType;
    message = null;
    const entry = RUN_CATALOG.flatMap((group) => group.entries).find((candidate) => candidate.jobType === jobType);
    try {
      await createJob(jobType);
      message = `Queued ${entry?.label ?? jobLabelForType(jobType)}`;
      error = null;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to queue job";
    } finally {
      runningJobType = null;
    }
  }

  async function handleToggleGraph(graph: JobGraphSummary) {
    if (expandedGraphId === graph.id) {
      expandedGraphId = null;
      return;
    }
    expandedGraphId = graph.id;
    if (graphDetails[graph.id]) return;
    loadingGraphId = graph.id;
    try {
      const detail = await fetchJobGraph(graph.id, nsfw.mode === "off");
      graphDetails = { ...graphDetails, [graph.id]: detail };
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load graph detail";
    } finally {
      loadingGraphId = null;
    }
  }

  async function handleCancelGraph(graph: JobGraphSummary) {
    cancellingGraphId = graph.id;
    message = null;
    try {
      const result = await cancelJobGraph(graph.id);
      message = result.cancelled ? "Workflow cancelled" : "Workflow already finished";
      error = null;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to cancel workflow";
    } finally {
      cancellingGraphId = null;
    }
  }

  async function handleClearFailures() {
    clearingFailures = true;
    try {
      const result = await clearJobFailures();
      dismissedErrors.clearAll();
      message = `Cleared ${result.cleared} failed node${result.cleared === 1 ? "" : "s"}`;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to clear node failures";
    } finally {
      clearingFailures = false;
    }
  }
</script>

<svelte:head>
  <title>Jobs · Prismedia</title>
</svelte:head>

<div class="flex min-w-0 flex-col gap-6">
  <ManagePageHeader icon={Activity} title="Jobs">
    {#snippet status()}
      {#if workerHealth.status === "offline"}
        <span class="flex items-center gap-2 text-caption text-error-text" title={workerHealth.tooltip}>
          <StatusLed status={workerHealth.led} size="sm" />
          {workerHealth.label}
        </span>
      {/if}
      {#if !loading}
        <span class="flex flex-wrap gap-x-3 font-mono text-[0.72rem] text-text-muted">
          <span class={cn(totals.running > 0 && "text-text-primary")}>{totals.running} running</span>
          <span>{totals.queued} queued</span>
          {#if waitingCount > 0}<span class="text-warning-text">{waitingCount} waiting</span>{/if}
          <span class={cn(totals.failed > 0 && "text-error-text")}>{compact.format(totals.failed)} failed</span>
          <span>{compact.format(totals.completed)} done</span>
        </span>
        {#if lastScanAt || dashboard?.schedule.enabled}
          <span class="font-mono text-[0.72rem] text-text-muted">
            {#if lastScanAt}scan {formatRelativeTimeShort(lastScanAt)}{/if}
            {#if dashboard?.schedule.enabled}{lastScanAt ? "· " : "scan "}every {dashboard.schedule.intervalMinutes}m{/if}
          </span>
        {/if}
      {/if}
    {/snippet}
    {#snippet actions()}
      <Button variant="ghost" size="sm" onclick={() => void refreshNow()} disabled={refreshing}>
        <RefreshCw class={cn(refreshing && "animate-spin motion-reduce:animate-none")} aria-hidden="true" />
        Refresh
      </Button>
    {/snippet}
  </ManagePageHeader>

  <JobCommandBar onRun={(jobType) => void handleRun(jobType)} pendingType={runningJobType} />

  {#if error}
    <Alert.Root variant="destructive"><AlertTriangle /><Alert.Description>{error}</Alert.Description></Alert.Root>
  {:else if message}
    <Alert.Root role="status"><Alert.Description>{message}</Alert.Description></Alert.Root>
  {/if}

  {#if liveGraphs.length > 0}
    <section class="flex flex-col gap-2" aria-labelledby="jobs-now">
      <h2 id="jobs-now" class="font-heading text-base font-semibold text-text-primary">
        Now <span class="ml-1 font-mono text-caption font-normal text-text-muted">{liveGraphs.length}</span>
      </h2>
      <div class="flex flex-col gap-2">
        {#each liveGraphs as graph (graph.id)}
          <GraphLaneCard
            {graph}
            detail={graphDetails[graph.id]}
            expanded={expandedGraphId === graph.id}
            loadingDetail={loadingGraphId === graph.id}
            cancelling={cancellingGraphId === graph.id}
            onToggle={handleToggleGraph}
            onCancel={handleCancelGraph}
          />
        {/each}
      </div>
    </section>
  {/if}

  {#if visibleFailedGroups.length > 0}
    <section class="flex flex-col gap-2" aria-labelledby="jobs-failures">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 id="jobs-failures" class="font-heading text-base font-semibold text-text-primary">
          Failures <span class="ml-1 font-mono text-caption font-normal text-error-text">{visibleFailedGroups.length}</span>
        </h2>
        <Button variant="ghost" size="sm" disabled={clearingFailures} onclick={() => void handleClearFailures()}>
          {#if clearingFailures}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Ban aria-hidden="true" />{/if}
          Clear failures
        </Button>
      </div>
      <div class="flex flex-col gap-2">
        {#each shownFailedGroups as group (group.fingerprint)}
          <FailedJobCard
            job={group.representative}
            nsfwMode={nsfw.mode}
            occurrenceCount={group.count}
            fingerprint={group.fingerprint}
            onDismiss={(fingerprint) => dismissedErrors.dismiss(fingerprint)}
          />
        {/each}
      </div>
      {#if visibleFailedGroups.length > FAILURE_PREVIEW}
        <div>
          <Button variant="ghost" size="sm" onclick={() => (showAllFailures = !showAllFailures)}>
            {showAllFailures ? "Show fewer" : `Show ${visibleFailedGroups.length - FAILURE_PREVIEW} more`}
          </Button>
        </div>
      {/if}
    </section>
  {/if}

  {#if loading}
    <StatePlaceholder icon={Activity} title="Loading jobs" busy />
  {:else if lanes.length === 0}
    <StatePlaceholder icon={Activity} title="No background work" />
  {:else}
    <div class="flex flex-wrap items-center justify-end gap-x-4 gap-y-2 text-caption text-text-muted" aria-hidden="true">
      <span class="font-mono text-[0.68rem] text-text-disabled">24h · per hour</span>
      <ul class="flex flex-wrap gap-x-4 gap-y-1">
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
</div>
