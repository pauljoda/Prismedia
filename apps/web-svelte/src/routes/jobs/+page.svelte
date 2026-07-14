<script lang="ts">
  import { onDestroy, onMount } from "svelte";
  import {
    Activity,
    AlertTriangle,
    Ban,
    CheckCircle2,
    Clock,
    Cpu,
    Eye,
    Loader2,
    RefreshCw,
    Square,
  } from "@lucide/svelte";
  import { StatusLed, cn } from "@prismedia/ui-svelte";
  import {
    cancelJobRun,
    cancelJobs,
    clearJobFailures,
    createJob,
    fetchJobs,
    fetchWorkerHealth,
  } from "$lib/api/jobs";
  import { fetchSettingsValues } from "$lib/api/settings";
  import { settingKeys, valuesToLibrarySettings } from "$lib/settings/app-settings";
  import type { JobRun, JobsDashboard } from "$lib/jobs/models";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import {
    buildJobsDashboard,
    groupJobRunsByKind,
    jobTypesForQueue,
    type ScheduleInfo,
  } from "$lib/jobs/jobs-dashboard";
  import { RUN_CATALOG } from "$lib/jobs/run-catalog";
  import {
    describeRunResult,
    formatRelativeTimeShort,
    groupFailedJobs,
  } from "$lib/jobs/helpers";
  import {
    describeWorkerHealth,
    type WorkerHealthBadge,
  } from "$lib/jobs/worker-health";
  import { dismissedErrors } from "$lib/stores/dismissed-errors.svelte";
  import RunCatalogRow from "$lib/components/jobs/RunCatalogRow.svelte";
  import ActiveJobCard from "$lib/components/jobs/ActiveJobCard.svelte";
  import FailedJobCard from "$lib/components/jobs/FailedJobCard.svelte";
  import CompletedJobRow from "$lib/components/jobs/CompletedJobRow.svelte";
  import EmptyPanel from "$lib/components/jobs/EmptyPanel.svelte";

  let { data = {} }: { data?: { dashboard?: JobsDashboard | null } } = $props();

  const nsfw = useNsfw();

  let dashboard = $state<JobsDashboard | null>(null);
  let loading = $state(true);

  let runningJobType = $state<string | null>(null);
  let cancellingQueue = $state<string | null>(null);
  let cancellingAllJobs = $state(false);
  let cancellingJobRunId = $state<string | null>(null);
  let acknowledging = $state<"all" | string | null>(null);

  let error = $state<string | null>(null);
  let message = $state<string | null>(null);
  let workerHealth = $state<WorkerHealthBadge>(describeWorkerHealth(null));

  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let lastNsfwMode = $state(nsfw.mode);

  $effect(() => {
    dashboard = data.dashboard ?? null;
    loading = !data.dashboard;
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    dashboard = null;
    loading = true;
    void loadDashboard();
  });

  let scheduleInfo = $state<ScheduleInfo | undefined>(undefined);

  async function loadDashboard() {
    try {
      const response = await fetchJobs();
      dashboard = buildJobsDashboard(response.items, scheduleInfo, response.counts);
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load jobs";
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
      // best effort
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
      message = describeRunResult(jobType, 1, 0);
      error = null;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to queue job";
    } finally {
      runningJobType = null;
    }
  }

  async function handleCancelQueue(queueName: string) {
    const jobTypes = jobTypesForQueue(queueName);
    if (jobTypes.length === 0) {
      error = `${queueName} is not available in the worker yet.`;
      return;
    }

    cancellingQueue = queueName;
    message = null;
    try {
      let cancelled = 0;
      for (const jobType of jobTypes) {
        const response = await cancelJobs(jobType);
        cancelled += response.cancelled;
      }
      message = `Cancelled ${cancelled} job${cancelled === 1 ? "" : "s"}.`;
      error = null;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to cancel jobs";
    } finally {
      cancellingQueue = null;
    }
  }

  async function handleCancelAllJobs() {
    cancellingAllJobs = true;
    message = null;
    try {
      const response = await cancelJobs();
      message = `Cancelled ${response.cancelled} job${response.cancelled === 1 ? "" : "s"}.`;
      error = null;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to cancel jobs";
    } finally {
      cancellingAllJobs = false;
    }
  }

  async function handleCancelJob(job: JobRun) {
    cancellingJobRunId = job.id;
    message = null;
    try {
      const response = await cancelJobRun(job.id);
      message = `Cancelled ${response.cancelled} job${response.cancelled === 1 ? "" : "s"}.`;
      error = null;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to cancel job";
    } finally {
      cancellingJobRunId = null;
    }
  }

  async function handleAcknowledgeFailures(scope: "all" | string) {
    const jobTypes = scope === "all" ? [null] : jobTypesForQueue(scope);
    if (jobTypes.length === 0) {
      error = `${scope} is not available in the worker yet.`;
      return;
    }

    acknowledging = scope;
    message = null;
    try {
      let cleared = 0;
      for (const jobType of jobTypes) {
        const response = await clearJobFailures(jobType);
        cleared += response.cleared;
      }
      message = `Cleared ${cleared} failed job${cleared === 1 ? "" : "s"}.`;
      error = null;
      dismissedErrors.clearAll();
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to clear failures";
    } finally {
      acknowledging = null;
    }
  }

  const failedGroups = $derived(groupFailedJobs(dashboard?.failedJobs ?? []));
  const visibleFailedGroups = $derived(
    failedGroups.filter((group) => !dismissedErrors.isDismissed(group.fingerprint)),
  );
  const suppressedFailedCount = $derived(
    failedGroups.length - visibleFailedGroups.length,
  );
  const actionableFailedByQueue = $derived.by(() => {
    const counts = new Map<string, number>();
    for (const group of visibleFailedGroups) {
      const queueName = group.representative.queueName;
      counts.set(queueName, (counts.get(queueName) ?? 0) + 1);
    }
    return counts;
  });
  const queueByName = $derived.by(() => {
    const queues = dashboard?.queues ?? [];
    const map = new Map<string, (typeof queues)[number]>();
    for (const queue of queues) {
      const failed = actionableFailedByQueue.get(queue.name) ?? 0;
      const backlog = queue.waiting + queue.delayed;
      map.set(queue.name, {
        ...queue,
        failed,
        backlog,
        status: failed > 0 ? "warning" : queue.active + backlog > 0 ? "active" : "idle",
      });
    }
    return map;
  });

  const totalActive = $derived(
    dashboard?.queues.reduce((sum, q) => sum + q.active, 0) ?? 0,
  );
  const totalQueued = $derived(
    dashboard?.queues.reduce((sum, q) => sum + q.backlog, 0) ?? 0,
  );
  const rawFailedTotal = $derived(
    dashboard?.queues.reduce((sum, q) => sum + q.failed, 0) ?? 0,
  );
  const actionableFailedTotal = $derived(visibleFailedGroups.length);
  const canAcknowledgeFailures = $derived(
    actionableFailedTotal > 0,
  );
  const canShowCancelAllJobs = $derived(
    nsfw.mode !== "show" || totalActive + totalQueued > 0,
  );

  const runningJobs = $derived(
    (dashboard?.activeJobs ?? []).filter((j) => j.status === "active"),
  );
  const queuedJobs = $derived(
    (dashboard?.activeJobs ?? []).filter(
      (j) => j.status === "waiting" || j.status === "delayed",
    ),
  );
  const runningGroups = $derived(groupJobRunsByKind(runningJobs));
  const queuedGroups = $derived(groupJobRunsByKind(queuedJobs));

  const runningOverflow = $derived(Math.max(0, totalActive - runningJobs.length));
  const queuedOverflow = $derived(Math.max(0, totalQueued - queuedJobs.length));

  const allQuiet = $derived(
    !loading &&
      visibleFailedGroups.length === 0 &&
      suppressedFailedCount === 0 &&
      totalActive === 0 &&
      totalQueued === 0 &&
      (dashboard?.completedJobs.length ?? 0) === 0,
  );
</script>

<svelte:head>
  <title>Job Control · Prismedia</title>
</svelte:head>

<div class="space-y-5">
  <!-- ── Header ── -->
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
            workerHealth.status === "checking" && "is-checking",
          )}
          title={workerHealth.tooltip}
          aria-label={workerHealth.tooltip}
        >
          <StatusLed status={workerHealth.led} size="sm" pulse={workerHealth.pulse} />
          <span>{workerHealth.label}</span>
        </span>
      </div>
      <div class="mt-1.5 flex flex-wrap items-center gap-3 text-mono-sm">
        <span
          class={cn(
            "flex items-center gap-1.5",
            totalActive > 0 ? "text-text-accent" : "text-text-disabled",
          )}
        >
          <StatusLed
            status={totalActive > 0 ? "phosphor" : "idle"}
            size="sm"
            pulse={totalActive > 0}
          />
          {totalActive} active
        </span>
        <span
          class={cn(
            "flex items-center gap-1.5",
            totalQueued > 0 ? "text-text-muted" : "text-text-disabled",
          )}
        >
          <StatusLed
            status={totalQueued > 0 ? "warning" : "idle"}
            size="sm"
          />
          {totalQueued} queued
        </span>
        <span
          class={cn(
            "flex items-center gap-1.5",
            actionableFailedTotal > 0 ? "text-status-error-text" : "text-text-disabled",
          )}
        >
          <StatusLed
            status={actionableFailedTotal > 0 ? "error" : "idle"}
            size="sm"
          />
          {actionableFailedTotal} failed
        </span>
        <span class="text-text-disabled">
          <Clock class="inline-block h-3 w-3" />
          scan {formatRelativeTimeShort(dashboard?.lastScanAt ?? null)}
          {#if dashboard?.schedule.enabled}
            · auto {dashboard.schedule.intervalMinutes}m
          {/if}
        </span>
      </div>
    </div>
    <div class="flex flex-wrap items-center gap-1.5">
      {#if canAcknowledgeFailures}
        <button
          type="button"
          onclick={() => void handleAcknowledgeFailures("all")}
          disabled={acknowledging !== null}
          class="flex items-center gap-1.5 rounded-xs px-2.5 py-1.5 text-xs text-text-muted transition-colors hover:bg-status-error/10 hover:text-status-error-text disabled:opacity-40"
        >
          <Ban class="h-3.5 w-3.5" />
          {acknowledging === "all" ? "Clearing…" : "Clear all failures"}
        </button>
      {/if}
      {#if canShowCancelAllJobs}
        <button
          type="button"
          onclick={() => void handleCancelAllJobs()}
          disabled={cancellingAllJobs || loading}
          class="flex items-center gap-1.5 rounded-xs px-2.5 py-1.5 text-xs text-text-muted transition-colors hover:bg-status-error/10 hover:text-status-error-text disabled:opacity-40"
        >
          <Square class="h-3.5 w-3.5" />
          {cancellingAllJobs ? "Killing…" : "Kill all"}
        </button>
      {/if}
      <button
        type="button"
        onclick={() => void loadDashboard()}
        class="flex items-center gap-1.5 rounded-xs px-2.5 py-1.5 text-xs text-text-muted transition-colors hover:bg-surface-3/60 hover:text-text-primary"
      >
        <RefreshCw class="h-3.5 w-3.5" />
        Refresh
      </button>
    </div>
  </div>

  <!-- ── Toasts ── -->
  {#if error}
    <div class="surface-panel border-l-2 border-status-error px-3 py-2 text-sm text-status-error-text">
      {error}
    </div>
  {/if}
  {#if message && !error}
    <div class="surface-panel border-l-2 border-status-success px-3 py-2 text-sm text-status-success-text">
      {message}
    </div>
  {/if}

  <!-- ── Run a job ── -->
  <section class="surface-panel p-4">
    <div class="grid gap-4 md:grid-cols-2">
      {#each RUN_CATALOG as group (group.id)}
        <div>
          <div class="mb-1.5 px-2 text-[0.58rem] font-semibold uppercase tracking-[0.15em] text-text-disabled">
            {group.title}
          </div>
          <div class="surface-well p-1.5 space-y-0.5">
            {#each group.entries as entry (entry.jobType)}
              <RunCatalogRow
                {entry}
                queue={queueByName.get(entry.queueName)}
                running={runningJobType === entry.jobType}
                stopping={cancellingQueue === entry.queueName}
                clearing={acknowledging === entry.queueName}
                disabled={runningJobType !== null && runningJobType !== entry.jobType}
                onRun={handleRun}
                onStop={handleCancelQueue}
                onClearFailures={(queueName) => handleAcknowledgeFailures(queueName)}
              />
            {/each}
          </div>
        </div>
      {/each}
    </div>
  </section>

  <!-- ── Activity stream ── -->

  {#if allQuiet}
    <EmptyPanel
      title="All quiet"
      detail="No active work, no failures, no recent jobs. Run a job from the switchboard to get started."
    />
  {/if}

  <!-- Running now — hero section -->
  {#if totalActive > 0}
    <section class="space-y-2">
      <div class="flex items-center justify-between px-1">
        <div class="flex items-center gap-2">
          <Loader2 class="h-4 w-4 animate-spin text-text-accent" />
          <h2 class="text-kicker text-text-accent">Running now</h2>
          <span class="text-mono-sm text-text-disabled">
            {totalActive}{#if runningOverflow > 0} · {runningJobs.length} shown{/if}
          </span>
        </div>
      </div>
      {#if runningJobs.length > 0}
        <div
          class={cn(
            "surface-card overflow-hidden transition-shadow duration-[2400ms]",
            "border-border-accent shadow-[var(--shadow-glow-accent)]",
          )}
        >
          {#each runningGroups as group (group.key)}
            <div class="border-b border-border-subtle/50 last:border-0">
              <div class="flex items-start justify-between gap-3 bg-surface-2/45 px-3 py-2">
                <div class="min-w-0">
                  <h3 class="truncate text-[0.72rem] font-semibold uppercase tracking-[0.12em] text-text-accent">
                    {group.jobLabel}
                  </h3>
                  <p class="mt-0.5 line-clamp-1 text-[0.68rem] text-text-disabled">
                    {group.jobDescription}
                  </p>
                </div>
                <span class="shrink-0 text-[0.65rem] text-text-disabled">
                  {group.activeCount} running
                </span>
              </div>
              {#each group.jobs as job (job.id)}
                <ActiveJobCard
                  {job}
                  nsfwMode={nsfw.mode}
                  {cancellingJobRunId}
                  onCancelJob={handleCancelJob}
                />
              {/each}
            </div>
          {/each}
          {#if runningOverflow > 0}
            <div class="border-t border-border-subtle/40 bg-surface-2/30 px-3 py-1.5 text-center text-[0.65rem] text-text-disabled">
              + {runningOverflow} more running not shown · use queue stop to halt
            </div>
          {/if}
        </div>
      {:else}
        <div class="surface-card no-lift px-3 py-3 text-center text-[0.72rem] text-text-disabled">
          {totalActive} active job{totalActive === 1 ? "" : "s"} not yet visible in recent runs.
        </div>
      {/if}
    </section>
  {/if}

  <!-- Queued -->
  {#if totalQueued > 0}
    <section class="space-y-2">
      <div class="flex items-center justify-between px-1">
        <div class="flex items-center gap-2">
          <Cpu class="h-4 w-4 text-text-muted" />
          <h2 class="text-kicker text-text-muted">Queued</h2>
          <span class="text-mono-sm text-text-disabled">
            {totalQueued}{#if queuedOverflow > 0} · {queuedJobs.length} shown{/if}
          </span>
        </div>
      </div>
      {#if queuedJobs.length > 0}
        <div class="surface-card no-lift overflow-hidden">
          {#each queuedGroups as group (group.key)}
            <div class="border-b border-border-subtle/50 last:border-0">
              <div class="flex items-start justify-between gap-3 bg-surface-2/35 px-3 py-2">
                <div class="min-w-0">
                  <h3 class="truncate text-[0.72rem] font-semibold uppercase tracking-[0.12em] text-text-muted">
                    {group.jobLabel}
                  </h3>
                  <p class="mt-0.5 line-clamp-1 text-[0.68rem] text-text-disabled">
                    {group.jobDescription}
                  </p>
                </div>
                <span class="shrink-0 text-[0.65rem] text-text-disabled">
                  {group.waitingCount} queued
                </span>
              </div>
              {#each group.jobs as job (job.id)}
                <ActiveJobCard
                  {job}
                  nsfwMode={nsfw.mode}
                  {cancellingJobRunId}
                  onCancelJob={handleCancelJob}
                />
              {/each}
            </div>
          {/each}
          {#if queuedOverflow > 0}
            <div class="border-t border-border-subtle/40 bg-surface-2/30 px-3 py-1.5 text-center text-[0.65rem] text-text-disabled">
              + {queuedOverflow} more queued not shown
            </div>
          {/if}
        </div>
      {:else}
        <div class="surface-card no-lift px-3 py-3 text-center text-[0.72rem] text-text-disabled">
          {totalQueued} queued job{totalQueued === 1 ? "" : "s"} waiting.
        </div>
      {/if}
    </section>
  {/if}

  <!-- Failures -->
  {#if visibleFailedGroups.length > 0 || suppressedFailedCount > 0}
    <section class="space-y-2">
      <div class="flex items-center justify-between px-1">
        <div class="flex items-center gap-2">
          <AlertTriangle class="h-4 w-4 text-status-error-text" />
          <h2 class="text-kicker text-status-error-text">Needs attention</h2>
          <span class="text-mono-sm text-text-disabled">
            {visibleFailedGroups.length} shown{#if rawFailedTotal > visibleFailedGroups.length} · {rawFailedTotal} total{/if}
          </span>
        </div>
        {#if suppressedFailedCount > 0}
          <button
            type="button"
            onclick={() => dismissedErrors.clearAll()}
            class="flex items-center gap-1 rounded-xs px-2 py-1 text-[0.7rem] text-text-disabled transition-colors hover:bg-surface-2/40 hover:text-text-primary"
          >
            <Eye class="h-3 w-3" />
            Show {suppressedFailedCount} suppressed
          </button>
        {/if}
      </div>
      {#each visibleFailedGroups as group (group.fingerprint)}
        <FailedJobCard
          job={group.representative}
          nsfwMode={nsfw.mode}
          occurrenceCount={group.count}
          fingerprint={group.fingerprint}
          onDismiss={(fp) => dismissedErrors.dismiss(fp)}
        />
      {/each}
      {#if visibleFailedGroups.length === 0 && suppressedFailedCount > 0}
        <div class="surface-card no-lift px-3 py-4 text-center text-[0.72rem] text-text-disabled">
          All current failures are suppressed. Use <span class="text-text-muted">Show {suppressedFailedCount} suppressed</span> to review them.
        </div>
      {/if}
    </section>
  {/if}

  <!-- Recently completed -->
  {#if (dashboard?.completedJobs.length ?? 0) > 0}
    <section class="space-y-2">
      <div class="flex items-center justify-between px-1">
        <div class="flex items-center gap-2">
          <CheckCircle2 class="h-4 w-4 text-text-disabled" />
          <h2 class="text-kicker text-text-muted">Recently completed</h2>
          <span class="text-mono-sm text-text-disabled">{dashboard?.completedJobs.length}</span>
        </div>
      </div>
      <div class="surface-card no-lift overflow-hidden">
        <div class="divide-y divide-border-subtle/50">
          {#each dashboard?.completedJobs ?? [] as job (job.id)}
            <CompletedJobRow {job} nsfwMode={nsfw.mode} />
          {/each}
        </div>
      </div>
    </section>
  {/if}
</div>

<style>
  .worker-status-badge {
    display: inline-flex;
    min-height: 1.45rem;
    align-items: center;
    gap: 0.4rem;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-xs);
    background:
      linear-gradient(135deg, rgba(255, 255, 255, 0.055), transparent 42%),
      color-mix(in srgb, var(--color-surface-2) 86%, var(--color-surface-1) 14%);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.055),
      0 2px 8px rgba(0, 0, 0, 0.32);
    color: var(--color-text-muted);
    padding: 0.26rem 0.58rem 0.24rem 0.46rem;
    font-family: var(--font-mono);
    font-size: 0.63rem;
    font-weight: 700;
    letter-spacing: 0.14em;
    line-height: 1;
    text-transform: uppercase;
    transition:
      border-color var(--duration-fast) var(--ease-default),
      box-shadow var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default);
  }

  .worker-status-badge.is-online {
    border-color: var(--color-border-accent);
    background:
      linear-gradient(135deg, rgba(199, 201, 204, 0.14), rgba(199, 201, 204, 0.035) 44%, transparent 100%),
      color-mix(in srgb, var(--color-surface-2) 82%, var(--color-accent-900) 18%);
    box-shadow:
      inset 0 1px 0 rgba(199, 201, 204, 0.13),
      var(--shadow-glow-accent);
    color: var(--color-text-accent);
  }

  .worker-status-badge.is-offline {
    border-color: rgba(255, 128, 111, 0.32);
    background:
      linear-gradient(135deg, rgba(255, 128, 111, 0.13), transparent 52%),
      rgba(74, 28, 24, 0.34);
    box-shadow:
      inset 0 1px 0 rgba(255, 159, 146, 0.1),
      0 0 18px rgba(255, 128, 111, 0.11);
    color: var(--color-error-text);
  }

  .worker-status-badge.is-checking {
    color: var(--color-text-muted);
  }
</style>
