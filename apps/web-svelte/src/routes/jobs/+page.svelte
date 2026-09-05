<script lang="ts">
  import { onDestroy, onMount } from "svelte";
  import {
    Activity,
    AlertTriangle,
    Ban,
    CheckCircle2,
    CirclePause,
    Clock,
    GitBranch,
    Loader2,
    RefreshCw,
  } from "@lucide/svelte";
  import { Button, Disclosure, StatusLed, cn } from "@prismedia/ui-svelte";
  import {
    cancelJobGraph,
    clearJobFailures,
    createJob,
    fetchJobGraph,
    fetchJobGraphs,
    fetchJobs,
    fetchWorkerHealth,
  } from "$lib/api/jobs";
  import type { JobGraphDetailResponse, JobGraphSummary } from "$lib/api/generated/model";
  import {
    JOB_GRAPH_STATUS,
  } from "$lib/api/generated/codes";
  import { fetchSettingsValues } from "$lib/api/settings";
  import { settingKeys, valuesToLibrarySettings } from "$lib/settings/app-settings";
  import type { JobsDashboard } from "$lib/jobs/models";
  import {
    buildJobsDashboard,
    groupJobGraphsByActivity,
    type ScheduleInfo,
  } from "$lib/jobs/jobs-dashboard";
  import { RUN_CATALOG } from "$lib/jobs/run-catalog";
  import {
    displayJobHeading,
    formatRelativeTimeShort,
    groupFailedJobs,
  } from "$lib/jobs/helpers";
  import {
    describeWorkerHealth,
    type WorkerHealthBadge,
  } from "$lib/jobs/worker-health";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { dismissedErrors } from "$lib/stores/dismissed-errors.svelte";
  import RunCatalogRow from "$lib/components/jobs/RunCatalogRow.svelte";
  import GraphLaneCard from "$lib/components/jobs/GraphLaneCard.svelte";
  import FailedJobCard from "$lib/components/jobs/FailedJobCard.svelte";
  import EmptyPanel from "$lib/components/jobs/EmptyPanel.svelte";

  const nsfw = useNsfw();

  let graphs = $state.raw<JobGraphSummary[]>([]);
  let graphDetails = $state.raw<Record<string, JobGraphDetailResponse>>({});
  let dashboard = $state.raw<JobsDashboard | null>(null);
  let scheduleInfo = $state<ScheduleInfo | undefined>(undefined);
  let loading = $state(true);
  let expandedGraphId = $state<string | null>(null);
  let loadingGraphId = $state<string | null>(null);
  let cancellingGraphId = $state<string | null>(null);
  let runningJobType = $state<string | null>(null);
  let clearingFailures = $state(false);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);
  let workerHealth = $state<WorkerHealthBadge>(describeWorkerHealth(null));
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let lastNsfwMode = $state(nsfw.mode);

  const graphGroups = $derived(groupJobGraphsByActivity(graphs));
  const activeGraphs = $derived(graphGroups.active);
  const waitingGraphs = $derived(graphGroups.waiting);
  const recentGraphs = $derived(graphGroups.recent);
  const runningCount = $derived(graphs.filter((graph) => graph.status === JOB_GRAPH_STATUS.running).length);
  const waitingCount = $derived(graphs.filter((graph) => graph.status === JOB_GRAPH_STATUS.waiting).length);
  const queuedCount = $derived(graphs.filter((graph) => graph.status === JOB_GRAPH_STATUS.queued).length);
  const failedCount = $derived(graphs.filter((graph) => graph.status === JOB_GRAPH_STATUS.failed).length);
  const warningCount = $derived(
    graphs.filter((graph) => graph.status === JOB_GRAPH_STATUS.completedWithWarnings).length,
  );
  const failedGroups = $derived(groupFailedJobs(dashboard?.failedJobs ?? []));
  const visibleFailedGroups = $derived(
    failedGroups.filter((group) => !dismissedErrors.isDismissed(group.fingerprint)),
  );
  const allQuiet = $derived(
    !loading &&
      activeGraphs.length === 0 &&
      waitingGraphs.length === 0 &&
      recentGraphs.length === 0 &&
      visibleFailedGroups.length === 0,
  );

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadDashboard();
  });

  async function loadDashboard() {
    try {
      const hideNsfw = nsfw.mode === "off";
      const [graphResponse, jobResponse] = await Promise.all([
        fetchJobGraphs(hideNsfw),
        fetchJobs(hideNsfw),
      ]);
      graphs = graphResponse.items;
      dashboard = buildJobsDashboard(jobResponse.items, scheduleInfo, jobResponse.counts);
      if (expandedGraphId) {
        const detail = await fetchJobGraph(expandedGraphId, hideNsfw);
        graphDetails = { ...graphDetails, [expandedGraphId]: detail };
      }
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load job lanes";
    } finally {
      loading = false;
    }
  }

  async function loadSchedule() {
    try {
      const config = await fetchSettingsValues([
        settingKeys.scanAutoScanEnabled,
        settingKeys.scanIntervalMinutes,
      ]);
      const settings = valuesToLibrarySettings(config.values);
      scheduleInfo = {
        enabled: settings.autoScanEnabled,
        intervalMinutes: settings.scanIntervalMinutes,
      };
    } catch {
      // The scheduler badge is informational; graph state remains authoritative.
    }
  }

  async function loadWorkerHealth() {
    try {
      workerHealth = describeWorkerHealth(await fetchWorkerHealth());
    } catch {
      workerHealth = describeWorkerHealth({
        status: "offline",
        workerId: null,
        lastSeenAt: null,
        staleAfterSeconds: 45,
      });
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
    }, 5000);
  });

  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });

  async function handleRun(jobType: string) {
    runningJobType = jobType;
    message = null;
    try {
      await createJob(jobType);
      message = "A background graph was queued.";
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
      message = result.cancelled ? "The workflow was cancelled." : "The workflow was already finished.";
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
      message = `Cleared ${result.cleared} failed node${result.cleared === 1 ? "" : "s"}.`;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to clear node failures";
    } finally {
      clearingFailures = false;
    }
  }
</script>

<svelte:head>
  <title>Job Control · Prismedia</title>
</svelte:head>

<div class="space-y-5">
  <div class="flex flex-wrap items-start justify-between gap-3">
    <div>
      <div class="flex flex-wrap items-center gap-2.5">
        <h1 class="flex items-center gap-2.5">
          <Activity class="h-5 w-5 text-text-accent" />
          Job Control
        </h1>
        <span
          class={cn(
            "worker-status-badge",
            workerHealth.status === "online" && "is-online",
            workerHealth.status === "offline" && "is-offline",
          )}
          title={workerHealth.tooltip}
        >
          <StatusLed status={workerHealth.led} size="sm" pulse={workerHealth.pulse} />
          {workerHealth.label}
        </span>
      </div>
      <div class="mt-1.5 flex flex-wrap items-center gap-3 text-mono-sm text-text-disabled">
        <span class={runningCount > 0 ? "text-text-accent" : undefined}>{runningCount} running</span>
        <span>{queuedCount} queued</span>
        <span class={waitingCount > 0 ? "text-status-warning-text" : undefined}>{waitingCount} waiting</span>
        <span class={failedCount > 0 ? "text-status-error-text" : undefined}>{failedCount} failed</span>
        {#if warningCount > 0}<span class="text-status-warning-text">{warningCount} with warnings</span>{/if}
        <span>
          <Clock class="inline-block h-3 w-3" />
          scan {formatRelativeTimeShort(dashboard?.lastScanAt ?? null)}
          {#if dashboard?.schedule.enabled} · auto {dashboard.schedule.intervalMinutes}m{/if}
        </span>
      </div>
    </div>
    <Button variant="ghost" size="sm" class="gap-1.5" onclick={() => void loadDashboard()} disabled={loading}>
      {#if loading}<Loader2 class="h-3.5 w-3.5 animate-spin" />{:else}<RefreshCw class="h-3.5 w-3.5" />{/if}
      Refresh
    </Button>
  </div>

  {#if error}
    <div class="surface-panel border-l-2 border-status-error px-3 py-2 text-sm text-status-error-text">{error}</div>
  {:else if message}
    <div class="surface-panel border-l-2 border-status-success px-3 py-2 text-sm text-status-success-text">{message}</div>
  {/if}

  <section class="surface-panel p-4">
    <div class="mb-3">
      <h2 class="text-kicker text-text-muted">Administrative work</h2>
      <p class="mt-1 text-xs text-text-disabled">
        These operations create background graphs governed by the configured background-worker limit.
      </p>
    </div>
    <div class="grid gap-4 md:grid-cols-2">
      {#each RUN_CATALOG as group (group.id)}
        <div>
          <div class="mb-1.5 px-2 text-[0.58rem] font-semibold uppercase tracking-[0.15em] text-text-disabled">{group.title}</div>
          <div class="surface-well space-y-0.5 p-1.5">
            {#each group.entries as entry (entry.jobType)}
              <RunCatalogRow
                {entry}
                running={runningJobType === entry.jobType}
                disabled={runningJobType !== null && runningJobType !== entry.jobType}
                onRun={handleRun}
              />
            {/each}
          </div>
        </div>
      {/each}
    </div>
  </section>

  {#if allQuiet}
    <EmptyPanel title="All quiet" detail="No active, waiting, or recent job workflows. Start an administrative task or an entity action to create one." />
  {/if}

  {#if activeGraphs.length > 0}
    <section class="space-y-2">
      <div class="flex items-center gap-2 px-1">
        <GitBranch class="h-4 w-4 text-text-accent" />
        <h2 class="text-kicker text-text-accent">Active execution lanes</h2>
        <span class="text-mono-sm text-text-disabled">{activeGraphs.length}</span>
      </div>
      <div class="space-y-2">
        {#each activeGraphs as graph (graph.id)}
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

  {#if waitingGraphs.length > 0}
    <section class="space-y-2">
      <div class="flex flex-wrap items-center justify-between gap-2 px-1">
        <div class="flex items-center gap-2">
          <CirclePause class="h-4 w-4 text-status-warning-text" />
          <h2 class="text-kicker text-status-warning-text">Waiting workflows</h2>
          <span class="text-mono-sm text-text-disabled">{waitingGraphs.length}</span>
        </div>
        <p class="text-[0.68rem] text-text-disabled">
          Waiting on review or an external event · no worker or active lane is held.
        </p>
      </div>
      <div class="space-y-2">
        {#each waitingGraphs as graph (graph.id)}
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

  {#if recentGraphs.length > 0}
    <section class="space-y-2">
      <div class="flex items-center gap-2 px-1">
        <CheckCircle2 class="h-4 w-4 text-text-muted" />
        <h2 class="text-kicker text-text-muted">Recent lanes</h2>
        <span class="text-mono-sm text-text-disabled">{recentGraphs.length}</span>
      </div>
      <div class="space-y-2">
        {#each recentGraphs as graph (graph.id)}
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

  <Disclosure title="Diagnostic job history" icon={Clock} count={dashboard?.recentJobs.length ?? 0}>
      {#if visibleFailedGroups.length > 0}
        <div class="mb-3 flex items-center justify-between gap-3">
          <div class="flex items-center gap-2">
            <AlertTriangle class="h-4 w-4 text-status-error-text" />
            <h3 class="text-kicker text-status-error-text">Failed nodes</h3>
          </div>
          <Button variant="ghost" size="sm" class="gap-1.5" disabled={clearingFailures} onclick={() => void handleClearFailures()}>
            {#if clearingFailures}<Loader2 class="h-3.5 w-3.5 animate-spin" />{:else}<Ban class="h-3.5 w-3.5" />{/if}
            Clear failures
          </Button>
        </div>
        <div class="space-y-2">
          {#each visibleFailedGroups as group (group.fingerprint)}
            <FailedJobCard
              job={group.representative}
              nsfwMode={nsfw.mode}
              occurrenceCount={group.count}
              fingerprint={group.fingerprint}
              onDismiss={(fingerprint) => dismissedErrors.dismiss(fingerprint)}
            />
          {/each}
        </div>
      {/if}

      <div class="mt-4 divide-y divide-border-subtle/50 rounded-xs border border-border-subtle">
        {#each dashboard?.recentJobs ?? [] as job (job.id)}
          <div class="flex items-center justify-between gap-3 px-3 py-2 text-xs">
            <div class="min-w-0">
              <div class="truncate text-text-primary">{displayJobHeading(job, nsfw.mode)}</div>
              <div class="mt-0.5 truncate text-text-disabled">{job.statusMessage ?? job.jobType}</div>
            </div>
            <span
              class={cn(
                "shrink-0 text-[0.62rem] uppercase tracking-[0.1em]",
                job.status === "failed" && "text-status-error-text",
                job.status === "active" && "text-text-accent",
                job.status === "completed" && "text-status-success-text",
              )}
            >
              {job.status}
            </span>
          </div>
        {/each}
        {#if (dashboard?.recentJobs.length ?? 0) === 0}
          <p class="px-3 py-4 text-center text-sm text-text-disabled">No node history is available.</p>
        {/if}
      </div>
  </Disclosure>
</div>

<style>
  .worker-status-badge {
    display: inline-flex;
    min-height: 1.45rem;
    align-items: center;
    gap: 0.4rem;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-xs);
    background: color-mix(in srgb, var(--color-surface-2) 86%, var(--color-surface-1) 14%);
    color: var(--color-text-muted);
    padding: 0.26rem 0.58rem 0.24rem 0.46rem;
    font-family: var(--font-mono);
    font-size: 0.63rem;
    font-weight: 700;
    letter-spacing: 0.14em;
    line-height: 1;
    text-transform: uppercase;
  }

  .worker-status-badge.is-online {
    border-color: var(--color-border-accent);
    box-shadow: var(--shadow-glow-accent);
    color: var(--color-text-accent);
  }

  .worker-status-badge.is-offline {
    border-color: rgba(255, 128, 111, 0.32);
    color: var(--color-error-text);
  }
</style>
