<script lang="ts">
  import { onMount } from "svelte";
  import { prefersReducedMotion } from "svelte/motion";
  import {
    Activity,
    FolderOpen,
    Gauge,
    HardDriveDownload,
    Plug,
    RefreshCw,
    ScanSearch,
    Send,
    ShieldUser,
  } from "@lucide/svelte";
  import { Button, StatusLed } from "@prismedia/ui-svelte";
  import type {
    ConnectionResponse,
    DownloadQueueItemView,
    JobGraphSummary,
    JobRun,
    PluginProvider,
    RequestActivityPage,
  } from "$lib/api/generated/model";
  import { fetchDownloadQueue } from "$lib/api/acquisitions";
  import { fetchConnections } from "$lib/api/connections";
  import { fetchEntities } from "$lib/api/entities";
  import { fetchIdentifyQueue } from "$lib/api/identify-client";
  import type { IdentifyQueueItem } from "$lib/api/identify-types";
  import { fetchJobGraphs, fetchJobs, fetchWorkerHealth } from "$lib/api/jobs";
  import { fetchPluginProviders } from "$lib/api/plugins";
  import { fetchRequestActivity } from "$lib/api/request-activity";
  import { fetchLibraryRoots, fetchSettingsValues, type LibraryRoot, type SettingValue } from "$lib/api/settings";
  import { createSerializedRefresh } from "$lib/async/serialized-refresh";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import PrismPageHeader from "$lib/components/concepts/PrismPageHeader.svelte";
  import ActivityStrip from "$lib/components/concepts/manage/ActivityStrip.svelte";
  import BandStrip from "$lib/components/concepts/manage/BandStrip.svelte";
  import LibraryRootCell from "$lib/components/concepts/manage/LibraryRootCell.svelte";
  import ManageConceptFrame from "$lib/components/concepts/manage/ManageConceptFrame.svelte";
  import OperateTile, { type TileFact } from "$lib/components/concepts/manage/OperateTile.svelte";
  import { bucketJobActivity } from "$lib/components/concepts/manage/job-activity";
  import { latestRootScan } from "$lib/components/concepts/manage/library-roots";
  import { bandsFromKinds } from "$lib/components/concepts/manage/media-families";
  import {
    downloadsReading,
    figure,
    formatEvery,
    identifyReading,
    integrationsReading,
    summarizeDownloads,
    summarizeIdentify,
    summarizeIntegrations,
    summarizeJobs,
    summarizeRequestActivity,
    type TileReading,
  } from "$lib/components/concepts/manage/operate-status";
  import { describeWorkerHealth, type WorkerHealthBadge } from "$lib/jobs/worker-health";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { settingKeys, valueAsBoolean, valueAsNumber } from "$lib/settings/app-settings";
  import { useSession } from "$lib/stores/session.svelte";
  import { formatRelativeTime } from "$lib/utils/format";

  const session = useSession();
  const nsfw = useNsfw();
  const hideNsfw = $derived(nsfw.mode === "off");

  /** Each system loads on its own so one slow or failing source never blanks the room. */
  const SOURCE = {
    jobs: "jobs",
    downloads: "downloads",
    wanted: "wanted",
    requests: "requests",
    identify: "identify",
    integrations: "integrations",
    libraries: "libraries",
    settings: "settings",
  } as const;
  type Source = (typeof SOURCE)[keyof typeof SOURCE];

  const POLL_MS = 15_000;

  let worker = $state<WorkerHealthBadge>(describeWorkerHealth(null));
  let graphs = $state.raw<JobGraphSummary[] | null>(null);
  let runs = $state.raw<JobRun[]>([]);
  let downloads = $state.raw<DownloadQueueItemView[] | null>(null);
  let wanted = $state<number | null>(null);
  let requestActivity = $state.raw<RequestActivityPage | null>(null);
  let identifyQueue = $state.raw<IdentifyQueueItem[] | null>(null);
  let plugins = $state.raw<PluginProvider[] | null>(null);
  let connections = $state.raw<ConnectionResponse[]>([]);
  let roots = $state.raw<LibraryRoot[] | null>(null);
  let settingValues = $state.raw<Record<string, SettingValue>>({});
  let errors = $state<Partial<Record<Source, string>>>({});
  let refreshing = $state(false);
  let checkedAt = $state<number | null>(null);
  let now = $state(Date.now());

  function settle<T>(source: Source, promise: Promise<T>, apply: (value: T) => void): Promise<void> {
    return promise.then(
      (value) => {
        apply(value);
        if (errors[source]) {
          const { [source]: _cleared, ...rest } = errors;
          errors = rest;
        }
      },
      (cause: unknown) => {
        errors = { ...errors, [source]: cause instanceof Error ? cause.message : "Could not load" };
      },
    );
  }

  async function load() {
    refreshing = true;
    const hidden = hideNsfw;
    await Promise.all([
      fetchWorkerHealth()
        .catch(() => ({ status: "offline", workerId: null, lastSeenAt: null, staleAfterSeconds: 45 }))
        .then((health) => (worker = describeWorkerHealth(health))),
      settle(SOURCE.jobs, Promise.all([fetchJobGraphs(hidden), fetchJobs(hidden)]), ([graphList, jobList]) => {
        graphs = graphList.items;
        runs = jobList.items;
      }),
      settle(SOURCE.downloads, fetchDownloadQueue(), (rows) => (downloads = rows)),
      settle(SOURCE.wanted, fetchEntities({ wanted: true, limit: 1, hideNsfw: hidden }), (page) => {
        wanted = Number(page.totalCount) || 0;
      }),
      settle(SOURCE.requests, fetchRequestActivity({ limit: 100, hideNsfw: hidden }), (page) => (requestActivity = page)),
      settle(SOURCE.identify, fetchIdentifyQueue(false, hidden), (queue) => (identifyQueue = queue)),
      settle(SOURCE.integrations, Promise.all([fetchPluginProviders(), fetchConnections()]), ([pluginList, connectionList]) => {
        plugins = pluginList;
        connections = connectionList;
      }),
      settle(SOURCE.libraries, fetchLibraryRoots(), (list) => (roots = list)),
      settle(
        SOURCE.settings,
        fetchSettingsValues([
          settingKeys.scanAutoScanEnabled,
          settingKeys.scanIntervalMinutes,
          settingKeys.monitoringSearchEnabled,
          settingKeys.monitoringIntervalMinutes,
        ]),
        (response) => (settingValues = response.values),
      ),
    ]);
    now = Date.now();
    checkedAt = now;
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

  const motionOk = $derived(!prefersReducedMotion.current);

  // ── Worker & jobs ──
  const jobs = $derived(graphs ? summarizeJobs(graphs, runs) : null);
  const activity = $derived(bucketJobActivity(runs, now));
  const visibleRoots = $derived((roots ?? []).filter((root) => !hideNsfw || !root.isNsfw));
  const lastScan = $derived(latestRootScan(visibleRoots));
  const jobsReading = $derived.by((): TileReading => {
    if (worker.status === "offline") return { led: "error", state: "Offline", urgent: true };
    if (worker.status === "checking") return { led: "warning", state: "Checking", urgent: false, pulse: true };
    if (jobs && jobs.failedRuns > 0) return { led: "warning", state: `${jobs.failedRuns} failed`, urgent: false };
    if (jobs && jobs.running > 0) return { led: "phosphor", state: "Working", urgent: false, pulse: true };
    return { led: "phosphor", state: "Online", urgent: false };
  });
  const jobsFacts = $derived.by((): TileFact[] => {
    if (!jobs) return [];
    return [
      { value: figure(jobs.waiting), label: "waiting" },
      { value: figure(jobs.failedRuns), label: "failed", tone: jobs.failedRuns > 0 ? "error" : undefined },
      { value: lastScan ? formatRelativeTime(lastScan, true) : "—", label: "since scan" },
    ];
  });

  // ── Downloads ──
  const downloadSummary = $derived(downloads ? summarizeDownloads(downloads) : null);
  const downloadBands = $derived(bandsFromKinds((downloads ?? []).map((row) => row.kind)));
  const downloadsState = $derived(downloadSummary ? downloadsReading(downloadSummary) : null);
  const downloadsFacts = $derived.by((): TileFact[] => {
    if (!downloadSummary) return [];
    return [
      { value: figure(downloadSummary.downloading), label: "transferring" },
      { value: figure(downloadSummary.queued + downloadSummary.searching), label: "queued" },
      { value: figure(downloadSummary.attention), label: "to choose", tone: downloadSummary.attention > 0 ? "warning" : undefined },
      { value: figure(downloadSummary.failed), label: "failed", tone: downloadSummary.failed > 0 ? "error" : undefined },
    ];
  });

  // ── Requests ──
  const requestCounts = $derived(summarizeRequestActivity(requestActivity));
  const requestSources = $derived(requestActivity?.sources.length ?? 0);
  const monitoringOn = $derived(valueAsBoolean(settingValues[settingKeys.monitoringSearchEnabled], false));
  const monitoringEvery = $derived(valueAsNumber(settingValues[settingKeys.monitoringIntervalMinutes], 0));
  const requestsState = $derived.by((): TileReading => {
    if (requestCounts.attention > 0) return { led: "warning", state: `${requestCounts.attention} to review`, urgent: true };
    if (requestCounts.progress > 0) return { led: "phosphor", state: "In progress", urgent: false, pulse: true };
    if (monitoringOn) return { led: "phosphor", state: "Monitoring", urgent: false };
    return { led: "idle", state: "Manual", urgent: false };
  });
  const requestsFacts = $derived<TileFact[]>([
    { value: monitoringOn ? formatEvery(monitoringEvery) : "off", label: "monitored search" },
    { value: figure(requestSources), label: requestSources === 1 ? "source" : "sources" },
    { value: figure(requestCounts.progress), label: "in progress" },
    { value: figure(requestCounts.attention), label: "to review", tone: requestCounts.attention > 0 ? "warning" : undefined },
  ]);

  // ── Identify ──
  const identify = $derived(identifyQueue ? summarizeIdentify(identifyQueue) : null);
  const identifyState = $derived(identify ? identifyReading(identify) : null);
  const identifyFacts = $derived.by((): TileFact[] => {
    if (!identify) return [];
    return [
      { value: figure(identify.working), label: "searching" },
      { value: figure(identify.failed), label: "failed", tone: identify.failed > 0 ? "error" : undefined },
      { value: figure(identify.total), label: "in queue" },
    ];
  });

  // ── Connections & plugins ──
  const visiblePlugins = $derived((plugins ?? []).filter((plugin) => !hideNsfw || !plugin.isNsfw));
  const integrations = $derived(plugins ? summarizeIntegrations(visiblePlugins, connections) : null);
  const integrationsState = $derived(integrations ? integrationsReading(integrations) : null);
  const litPlugins = $derived(visiblePlugins.filter((plugin) => plugin.installed && plugin.enabled));
  const integrationsFacts = $derived.by((): TileFact[] => {
    if (!integrations) return [];
    const needKeys = integrations.needsCredentials.length;
    return [
      { value: `${integrations.ready}/${integrations.connections}`, label: "connections ready", tone: integrations.broken > 0 ? "error" : undefined },
      { value: figure(needKeys), label: "need keys", tone: needKeys > 0 ? "warning" : undefined },
      { value: figure(integrations.updates), label: integrations.updates === 1 ? "update" : "updates" },
    ];
  });

  // ── Libraries ──
  const autoScanOn = $derived(valueAsBoolean(settingValues[settingKeys.scanAutoScanEnabled], false));
  const autoScanEvery = $derived(valueAsNumber(settingValues[settingKeys.scanIntervalMinutes], 0));
  const unscanned = $derived(visibleRoots.filter((root) => root.enabled && !root.lastScannedAt).length);
  const paused = $derived(visibleRoots.filter((root) => !root.enabled).length);
  const librariesState = $derived.by((): TileReading => {
    if (roots && visibleRoots.length === 0) return { led: "warning", state: "None", urgent: true };
    if (unscanned > 0) return { led: "warning", state: `${unscanned} unscanned`, urgent: true };
    return { led: "phosphor", state: "Watching", urgent: false };
  });
  const librariesFacts = $derived<TileFact[]>([
    { value: autoScanOn ? formatEvery(autoScanEvery) : "off", label: "auto-scan" },
    { value: lastScan ? formatRelativeTime(lastScan, true) : "—", label: "since scan" },
    { value: figure(paused), label: "paused" },
  ]);

  const readings = $derived(
    [jobsReading, downloadsState, requestsState, identifyState, integrationsState, roots ? librariesState : null].filter(
      (reading): reading is TileReading => reading !== null,
    ),
  );
  const needsYou = $derived(readings.filter((reading) => reading.urgent).length);
  const timeFormat = new Intl.DateTimeFormat(undefined, { hour: "numeric", minute: "2-digit", second: "2-digit" });
</script>

<svelte:head><title>Control room · Concepts · Prismedia</title></svelte:head>

<ManageConceptFrame concept="Control room">
  {#if !session.isAdmin}
    <StatePlaceholder icon={ShieldUser} title="Administrator access required" />
  {:else}
    <PrismPageHeader icon={Gauge} title="Control room">
      {#snippet status()}
        <span class="flex items-center gap-2 text-caption text-text-secondary">
          <StatusLed status={worker.led} size="sm" pulse={worker.pulse && motionOk} />
          {worker.label}
        </span>
        {#if checkedAt !== null}
          <span class="flex items-center gap-2 text-caption text-text-secondary">
            <StatusLed status={needsYou > 0 ? "warning" : "idle"} size="sm" />
            <span class="font-mono text-text-primary">{needsYou}</span> need attention
          </span>
          <span class="font-mono text-[0.68rem] text-text-disabled">{timeFormat.format(checkedAt)}</span>
        {/if}
      {/snippet}
      {#snippet actions()}
        <Button variant="ghost" size="sm" onclick={() => void refresh()} disabled={refreshing}>
          <RefreshCw class={refreshing && motionOk ? "animate-spin" : undefined} aria-hidden="true" />
          Refresh
        </Button>
      {/snippet}
    </PrismPageHeader>

    <div class="grid grid-flow-row-dense gap-3 md:grid-cols-2 xl:grid-cols-3">
      <OperateTile
        title="Worker & jobs"
        icon={Activity}
        led={errors.jobs ? "error" : jobsReading.led}
        stateLabel={errors.jobs ? "Unavailable" : jobsReading.state}
        pulse={(jobsReading.pulse ?? false) && motionOk}
        figure={jobs ? figure(jobs.running + jobs.queued) : errors.jobs ? "—" : null}
        unit={jobs && jobs.running + jobs.queued === 1 ? "active lane" : "active lanes"}
        facts={jobsFacts}
        action={{ label: "Job control", href: "/jobs" }}
        urgent={jobsReading.urgent}
      >
        {#snippet visual()}
          <ActivityStrip buckets={activity} height={26} axis label="Background runs" />
        {/snippet}
      </OperateTile>

      <OperateTile
        title="Downloads"
        icon={HardDriveDownload}
        led={errors.downloads ? "error" : (downloadsState?.led ?? "idle")}
        stateLabel={errors.downloads ? "Unavailable" : (downloadsState?.state ?? "Reading")}
        pulse={(downloadsState?.pulse ?? false) && motionOk}
        figure={downloadSummary ? figure(downloadSummary.total) : errors.downloads ? "—" : null}
        unit="in queue"
        facts={downloadsFacts}
        action={downloadSummary && downloadSummary.attention > 0
          ? { label: `Choose releases · ${downloadSummary.attention}`, href: "/downloads" }
          : { label: "Downloads", href: "/downloads" }}
        urgent={downloadsState?.urgent ?? false}
      >
        {#snippet visual()}
          {#if downloadBands.length > 0}
            <BandStrip bands={downloadBands} height={5} legend label="Queue by media family" />
          {/if}
        {/snippet}
      </OperateTile>

      <OperateTile
        title="Requests"
        icon={Send}
        led={errors.requests && errors.wanted ? "error" : requestsState.led}
        stateLabel={requestsState.state}
        pulse={(requestsState.pulse ?? false) && motionOk}
        figure={wanted !== null ? figure(wanted) : errors.wanted ? "—" : null}
        unit="wanted"
        facts={requestsFacts}
        action={{ label: "Request activity", href: "/request?activity" }}
        urgent={requestsState.urgent}
      />

      <OperateTile
        title="Libraries"
        icon={FolderOpen}
        class="md:col-span-2 xl:row-span-2"
        led={errors.libraries ? "error" : librariesState.led}
        stateLabel={errors.libraries ? "Unavailable" : roots ? librariesState.state : "Reading"}
        figure={roots ? figure(visibleRoots.length) : errors.libraries ? "—" : null}
        unit={visibleRoots.length === 1 ? "folder" : "folders"}
        facts={librariesFacts}
        action={{ label: "Libraries", href: "/settings/libraries" }}
        urgent={roots !== null && librariesState.urgent}
      >
        {#snippet visual()}
          {#if visibleRoots.length > 0}
            <ul class="grid gap-2 sm:grid-cols-2">
              {#each visibleRoots as root (root.id)}
                <LibraryRootCell {root} />
              {/each}
            </ul>
          {/if}
        {/snippet}
      </OperateTile>
      <OperateTile
        title="Identify queue"
        icon={ScanSearch}
        led={errors.identify ? "error" : (identifyState?.led ?? "idle")}
        stateLabel={errors.identify ? "Unavailable" : (identifyState?.state ?? "Reading")}
        pulse={(identifyState?.pulse ?? false) && motionOk}
        figure={identify ? figure(identify.review) : errors.identify ? "—" : null}
        unit="to review"
        facts={identifyFacts}
        action={{ label: "Identify", href: "/identify" }}
        urgent={identifyState?.urgent ?? false}
      />

      <OperateTile
        title="Connections & plugins"
        icon={Plug}
        class="md:col-span-2 xl:col-span-1"
        led={errors.integrations ? "error" : (integrationsState?.led ?? "idle")}
        stateLabel={errors.integrations ? "Unavailable" : (integrationsState?.state ?? "Reading")}
        figure={integrations ? figure(integrations.plugins) : errors.integrations ? "—" : null}
        unit={integrations?.plugins === 1 ? "plugin" : "plugins"}
        facts={integrationsFacts}
        action={integrations && integrations.needsCredentials.length > 0
          ? { label: "Add keys", href: "/plugins" }
          : { label: "Connections", href: "/settings/connections" }}
        urgent={integrationsState?.urgent ?? false}
      >
        {#snippet visual()}
          {#if litPlugins.length > 0}
            <ul class="flex flex-wrap gap-1.5" aria-label="Installed plugins">
              {#each litPlugins as plugin (plugin.id)}
                <li class="relative">
                  <PluginIcon name={plugin.name} iconUrl={plugin.iconUrl} class="size-8" />
                  {#if plugin.missingAuthKeys.length > 0}
                    <StatusLed status="warning" size="sm" class="absolute -right-0.5 -top-0.5" />
                  {/if}
                  <span class="sr-only">{plugin.name}{plugin.missingAuthKeys.length > 0 ? ", needs credentials" : ""}</span>
                </li>
              {/each}
            </ul>
          {/if}
        {/snippet}
      </OperateTile>

    </div>
  {/if}
</ManageConceptFrame>
