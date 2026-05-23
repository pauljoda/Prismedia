<script lang="ts">
  import { onDestroy, onMount } from "svelte";
  import {
    Activity,
    AlertTriangle,
    Ban,
    Clock,
    Cpu,
    Eye,
    ListChecks,
    RefreshCw,
    Square,
  } from "@lucide/svelte";
  import {
    cancelJobRun,
    cancelJobs,
    clearJobFailures,
    createJob,
    fetchJobs,
    fetchLibraryConfig,
  } from "$lib/api/prismedia";
  import type { JobRun, JobsDashboard } from "$lib/jobs/models";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import {
    buildJobsDashboard,
    jobTypeForQueue,
    jobTypesForQueue,
    type ScheduleInfo,
  } from "$lib/jobs/jobs-dashboard";
  import { groupQueuesForJobDashboard } from "$lib/jobs/queue-sections";
  import {
    describeRunResult,
    displayJobHeading,
    errorFingerprint,
    formatRelativeTimeShort,
  } from "$lib/jobs/helpers";
  import { dismissedErrors } from "$lib/stores/dismissed-errors.svelte";
  import OverviewStat from "$lib/components/jobs/OverviewStat.svelte";
  import QueueCard from "$lib/components/jobs/QueueCard.svelte";
  import ActiveJobCard from "$lib/components/jobs/ActiveJobCard.svelte";
  import FailedJobCard from "$lib/components/jobs/FailedJobCard.svelte";
  import CompletedJobRow from "$lib/components/jobs/CompletedJobRow.svelte";
  import EmptyPanel from "$lib/components/jobs/EmptyPanel.svelte";

  let { data = {} }: { data?: { dashboard?: JobsDashboard | null } } = $props();

  const nsfw = useNsfw();

  let dashboard = $state<JobsDashboard | null>(null);
  let loading = $state(true);

  let runningQueue = $state<string | null>(null);
  let cancellingQueue = $state<string | null>(null);
  let cancellingAllJobs = $state(false);
  let cancellingJobRunId = $state<string | null>(null);
  let acknowledging = $state<"all" | string | null>(null);

  let error = $state<string | null>(null);
  let message = $state<string | null>(null);

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
      const config = await fetchLibraryConfig();
      scheduleInfo = {
        enabled: config.settings.autoScanEnabled,
        intervalMinutes: config.settings.scanIntervalMinutes,
      };
    } catch {
      // schedule info is best-effort; jobs still load fine without it
    }
  }

  onMount(() => {
    dismissedErrors.init();
    void loadSchedule();
    void loadDashboard();
    pollTimer = setInterval(() => void loadDashboard(), 5000);
  });

  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });

  async function handleRun(queueName: string) {
    const jobType = jobTypeForQueue(queueName);
    if (!jobType) {
      error = `${queueName} is not available in the worker yet.`;
      return;
    }

    runningQueue = queueName;
    message = null;
    try {
      await createJob(jobType);
      message = describeRunResult(queueName, 1, 0);
      error = null;
      await loadDashboard();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to queue jobs";
    } finally {
      runningQueue = null;
    }
  }

  async function handleCancel(queueName: string) {
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
      message = `Cancelled ${cancelled} ${queueName} job${cancelled === 1 ? "" : "s"}.`;
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

  const queueSections = $derived(groupQueuesForJobDashboard(dashboard?.queues ?? []));
  const totalActive = $derived(
    dashboard?.activeJobs.filter((job) => job.status === "active").length ?? 0,
  );
  const totalQueued = $derived(
    dashboard?.queues.reduce((sum, queue) => sum + queue.backlog, 0) ?? 0,
  );
  const trueFailedTotal = $derived(
    dashboard?.queues.reduce((sum, q) => sum + q.failed, 0) ?? 0,
  );
  const visibleFailedJobs = $derived(
    (dashboard?.failedJobs ?? []).filter(
      (job) => !dismissedErrors.isDismissed(errorFingerprint(job)),
    ),
  );
  const suppressedFailedCount = $derived(
    (dashboard?.failedJobs ?? []).length - visibleFailedJobs.length,
  );
  const canAcknowledgeFailures = $derived(
    trueFailedTotal > 0 || (dashboard?.queues ?? []).some((q) => q.failed > 0),
  );

  // Group active jobs by queue for dense display
  const groupedActiveJobs = $derived.by(() => {
    const jobs = dashboard?.activeJobs ?? [];
    if (jobs.length === 0) return [];
    const groups = new Map<string, { queueLabel: string; jobs: typeof jobs }>();
    for (const job of jobs) {
      const existing = groups.get(job.queueName);
      if (existing) {
        existing.jobs.push(job);
      } else {
        groups.set(job.queueName, { queueLabel: job.queueLabel, jobs: [job] });
      }
    }
    return [...groups.entries()]
      .map(([queueName, { queueLabel, jobs: qJobs }]) => ({
        queueName,
        queueLabel,
        jobs: qJobs,
        activeCount: qJobs.filter((j) => j.status === "active").length,
        waitingCount: qJobs.filter((j) => j.status === "waiting" || j.status === "delayed").length,
      }))
      .sort((a, b) => b.activeCount - a.activeCount || b.waitingCount - a.waitingCount);
  });
</script>

<svelte:head>
  <title>Job Control · Prismedia</title>
</svelte:head>

<div class="space-y-6">
  <div class="flex flex-wrap items-start justify-between gap-3">
    <div>
      <h1 class="flex items-center gap-2.5">
        <Activity class="h-5 w-5 text-text-accent" />
        Job Control
      </h1>
      <p class="mt-1 text-text-muted text-[0.8rem]">
        Clear queue pressure, inspect live work, and keep only the failures that still need
        action.
      </p>
    </div>
    <div class="flex flex-wrap items-center gap-1.5">
      {#if canAcknowledgeFailures}
        <button
          type="button"
          onclick={() => void handleAcknowledgeFailures("all")}
          disabled={acknowledging !== null}
          class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted transition-all duration-fast hover:bg-status-error/10 hover:text-status-error-text disabled:opacity-40"
        >
          <Ban class="h-3.5 w-3.5" />
          {acknowledging === "all" ? "Clearing..." : "Clear all failures"}
        </button>
      {/if}
      <button
        type="button"
        onclick={() => void loadDashboard()}
        class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-text-muted transition-all duration-fast hover:bg-surface-3/60 hover:text-text-primary"
      >
        <RefreshCw class="h-3.5 w-3.5" />
        Refresh
      </button>
    </div>
  </div>

  {#if error}
    <div
      class="surface-card no-lift border-l-2 border-status-error px-3 py-2 text-sm text-status-error-text"
    >
      {error}
    </div>
  {/if}
  {#if message && !error}
    <div
      class="surface-card no-lift border-l-2 border-status-success px-3 py-2 text-sm text-status-success-text"
    >
      {message}
    </div>
  {/if}

  <!-- Overview stats -->
  <div class="grid grid-cols-2 gap-2 md:grid-cols-4">
    <OverviewStat
      icon={Cpu}
      label="Running"
      value={totalActive}
      detail={totalActive > 0 ? "Workers are active now" : "No worker pressure right now"}
      accent={totalActive > 0}
    />
    <OverviewStat
      icon={Clock}
      label="Backlog"
      value={totalQueued}
      detail={totalQueued > 0 ? "Queued or delayed work" : "No queued backlog"}
      accent={totalQueued > 0}
    />
    <OverviewStat
      icon={AlertTriangle}
      label="Failures"
      value={trueFailedTotal}
      detail={trueFailedTotal > 0 ? "Needs review or clearing" : "No uncleared failures"}
      accent={trueFailedTotal > 0}
      danger={trueFailedTotal > 0}
    />
    <OverviewStat
      icon={ListChecks}
      label="Last Scan"
      value={formatRelativeTimeShort(dashboard?.lastScanAt ?? null)}
      detail={dashboard?.schedule.enabled
        ? `Auto scan every ${dashboard.schedule.intervalMinutes}m`
        : "Auto scan disabled"}
    />
  </div>

  <div class="border-t border-border-subtle"></div>

  <!-- Queues -->
  <section class="space-y-3">
    <div class="flex items-center justify-between px-1">
      <div class="flex items-center gap-2.5">
        <Activity class="h-4 w-4 text-text-accent" />
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Queues
        </h2>
      </div>
      <span class="text-mono-sm text-text-disabled">
        {dashboard?.queues.length ?? 0} configured
      </span>
    </div>
    <div class="space-y-8">
      {#each queueSections as { section, queues: sectionQueues } (section?.id ?? "additional")}
        <div class="space-y-3">
          <div class="border-b border-border-subtle/80 px-1 pb-2">
            <h3
              class="text-[0.72rem] font-semibold tracking-[0.14em] font-heading text-text-primary uppercase"
            >
              {section?.title ?? "Additional queues"}
            </h3>
            <p class="mt-1 text-[0.68rem] text-text-muted">
              {section?.description ??
                "Queues not yet assigned to a section; layout may need an update."}
            </p>
          </div>
          <div class="grid grid-cols-1 gap-3 xl:grid-cols-2">
            {#each sectionQueues as queue (queue.name)}
              <QueueCard
                {queue}
                {runningQueue}
                {cancellingQueue}
                {acknowledging}
                onRun={handleRun}
                onCancel={handleCancel}
                onClearFailures={handleAcknowledgeFailures}
              />
            {/each}
          </div>
        </div>
      {/each}
      {#if !dashboard && loading}
        <div class="surface-card no-lift p-6 text-center text-sm text-text-muted">
          Loading queue state...
        </div>
      {/if}
    </div>
  </section>

  <div class="border-t border-border-subtle"></div>

  <!-- Live Work -->
  <section class="space-y-3">
    <div class="flex items-center justify-between px-1">
      <div class="flex items-center gap-2.5">
        <Cpu class="h-4 w-4 text-text-accent" />
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Live Work
        </h2>
      </div>
      <div class="flex items-center gap-2">
        {#if (dashboard?.activeJobs.length ?? 0) > 0}
          <button
            type="button"
            onclick={() => void handleCancelAllJobs()}
            disabled={cancellingAllJobs}
            class="flex items-center gap-1 px-2 py-1 text-xs text-text-muted transition-colors hover:text-status-error-text disabled:opacity-40"
          >
            <Square class="h-3 w-3" />
            {cancellingAllJobs ? "Killing..." : "Kill all"}
          </button>
        {/if}
        <span class="text-mono-sm text-text-disabled">
          {dashboard?.activeJobs.length ?? 0} visible
        </span>
      </div>
    </div>
    {#if groupedActiveJobs.length}
      <div class="surface-card no-lift overflow-hidden">
        {#each groupedActiveJobs as { queueName, queueLabel, jobs: qJobs, activeCount, waitingCount } (queueName)}
          <div class="border-b border-border-subtle/50 last:border-0">
            <div class="flex items-center justify-between bg-surface-2/50 px-3 py-1.5">
              <span
                class="text-[0.68rem] font-semibold uppercase tracking-[0.1em] text-text-muted"
              >
                {queueLabel}
              </span>
              <span class="text-[0.65rem] text-text-disabled">
                {#if activeCount > 0}{activeCount} running{/if}{#if activeCount > 0 && waitingCount > 0} · {/if}{#if waitingCount > 0}{waitingCount} queued{/if}
              </span>
            </div>
            {#each qJobs as job (job.id)}
              <ActiveJobCard
                {job}
                nsfwMode={nsfw.mode}
                {cancellingJobRunId}
                onCancelJob={handleCancelJob}
              />
            {/each}
          </div>
        {/each}
      </div>
    {:else}
      <EmptyPanel
        title="No active or queued jobs"
        detail="When work is triggered, the active queue and backlog will show up here first."
      />
    {/if}
  </section>

  <div class="border-t border-border-subtle"></div>

  <!-- Failures -->
  <section class="space-y-3">
    <div class="flex items-center justify-between px-1">
      <div class="flex items-center gap-2.5">
        <AlertTriangle class="h-4 w-4 text-status-error-text" />
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Failures
        </h2>
      </div>
      <div class="flex items-center gap-2">
        {#if trueFailedTotal > 0}
          <button
            type="button"
            onclick={() => void handleAcknowledgeFailures("all")}
            disabled={acknowledging !== null}
            class="flex items-center gap-1 px-2 py-1 text-xs text-text-muted transition-colors hover:text-status-error-text disabled:opacity-40"
          >
            <Ban class="h-3 w-3" />
            {acknowledging === "all" ? "Clearing..." : "Clear all"}
          </button>
        {/if}
        <span class="text-mono-sm text-text-disabled">
          {visibleFailedJobs.length} shown
          {#if trueFailedTotal > (dashboard?.failedJobs ?? []).length}
            · {trueFailedTotal} total
          {/if}
        </span>
      </div>
    </div>
    <div class="space-y-2">
      {#if visibleFailedJobs.length}
        {#each visibleFailedJobs as job (job.id)}
          <FailedJobCard
            {job}
            nsfwMode={nsfw.mode}
            onDismiss={(fp) => dismissedErrors.dismiss(fp)}
          />
        {/each}
      {:else if dashboard?.failedJobs.length && suppressedFailedCount > 0}
        <!-- All visible errors are suppressed -->
      {:else}
        <EmptyPanel
          title="No active failures"
          detail="Failed jobs stay here until you clear them, so this list should stay short and actionable."
        />
      {/if}
      {#if suppressedFailedCount > 0}
        <div
          class="flex items-center justify-between px-1 py-1.5 text-[0.72rem] text-text-disabled"
        >
          <span
            >{suppressedFailedCount} error type{suppressedFailedCount === 1
              ? ""
              : "s"} suppressed</span
          >
          <button
            type="button"
            onclick={() => dismissedErrors.clearAll()}
            class="flex items-center gap-1 text-text-muted transition-colors hover:text-text-primary"
          >
            <Eye class="h-3 w-3" />
            Show all
          </button>
        </div>
      {/if}
    </div>
  </section>

  <div class="border-t border-border-subtle"></div>

  <!-- Recently Finished -->
  <section class="space-y-3">
    <div class="flex items-center justify-between px-1">
      <div class="flex items-center gap-2.5">
        <ListChecks class="h-4 w-4 text-text-accent" />
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Recently Finished
        </h2>
      </div>
      <span class="text-mono-sm text-text-disabled"
        >{dashboard?.completedJobs.length ?? 0} shown</span
      >
    </div>
    <div class="surface-card no-lift overflow-hidden">
      <div class="divide-y divide-border-subtle/50">
        {#if dashboard?.completedJobs.length}
          {#each dashboard.completedJobs as job (job.id)}
            <CompletedJobRow {job} nsfwMode={nsfw.mode} />
          {/each}
        {:else}
          <div class="px-4 py-6 text-center text-sm text-text-disabled">
            No recent completions.
          </div>
        {/if}
      </div>
    </div>
  </section>
</div>
