<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { Activity, AlertTriangle, ArrowUpRight, CheckCircle2, History, RefreshCw, RotateCcw, X } from "@lucide/svelte";
  import { Alert, Badge, Button, Panel, Select, buttonVariants } from "@prismedia/ui-svelte";
  import { INTEGRATION_OPERATION, INTEGRATION_TRANSFER_MODE, INTEGRATION_TRANSFER_PHASE, MANAGED_REQUEST_PHASE, MANAGED_TRACKING_STATUS, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type { ConnectionResponse, IntegrationTransferResponse, ManagedRequestResponse, ManagedTrackingResponse, PluginIntegrationCapabilityOperationsItem, RequestActivityItem as RequestActivityRecord, RequestActivitySource } from "$lib/api/generated/model";
  import { cancelPublicationTransfer, retryPublicationTransfer } from "$lib/api/integration-transfers";
  import { fetchEntityThumbnails } from "$lib/api/entities";
  import { refreshTracking } from "$lib/api/managed-libraries";
  import { cancelRequest, refreshRequest } from "$lib/api/managed-requests";
  import { fetchRequestActivity } from "$lib/api/request-activity";
  import ManagedHoldingControls from "$lib/components/integrations/ManagedHoldingControls.svelte";
  import ManagedHoldingRelease from "$lib/components/integrations/ManagedHoldingRelease.svelte";
  import SourceAttribution from "$lib/components/integrations/SourceAttribution.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import { resolveEntityHrefById } from "$lib/entities/entity-route-resolver";
  import { isTransferTerminal, transferCancelLabel, transferStatusLabel, transferRetryLabel, transferProblem } from "$lib/integrations/transfer-labels";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { managedRequestActivityGroup, managedTrackingActivityGroup, REQUEST_ACTIVITY_GROUP, transferActivityGroup, type RequestActivityGroup } from "$lib/requests/request-activity";
  import { formatRelativeTime } from "$lib/utils/format";

  const ITEM = { transfer: "transfer", request: "request", holding: "holding" } as const;
  const PAGE_SIZE = 50;
  const RECENT_PREVIEW_COUNT = 6;
  type TransferItem = { type: typeof ITEM.transfer; key: string; group: RequestActivityGroup; timestamp: string; connection: ConnectionResponse | null; transfer: IntegrationTransferResponse };
  type RequestItem = { type: typeof ITEM.request; key: string; group: RequestActivityGroup; timestamp: string; connection: ConnectionResponse; request: ManagedRequestResponse };
  type HoldingItem = { type: typeof ITEM.holding; key: string; group: RequestActivityGroup; timestamp: string; connection: ConnectionResponse; holding: ManagedTrackingResponse };
  type ActivityItem = TransferItem | RequestItem | HoldingItem;

  let { connections }: { connections: ConnectionResponse[] } = $props();
  const nsfw = useNsfw();
  let connectionId = $state("");
  let records = $state<RequestActivityRecord[]>([]);
  let sources = $state<RequestActivitySource[]>([]);
  let nextCursor = $state<string | null>(null);
  let error = $state<string | null>(null);
  let actionError = $state<string | null>(null);
  let loading = $state(true);
  let loadingMore = $state(false);
  let busyKey = $state<string | null>(null);
  let expandedKey = $state<string | null>(null);
  let showAllRecent = $state(false);
  let visibleImportedEntityIds = $state<Set<string> | null>(null);
  let importedVisibilityScope = "";
  let importedVisibilitySequence = 0;
  let alive = true;
  let loadSequence = 0;
  let loadedNsfwMode = nsfw.mode;

  const connectionLookup = $derived(new Map(connections.map(connection => [connection.id, connection])));
  const activityItems = $derived.by(() => {
    const items: ActivityItem[] = [];
    for (const record of records) {
      const connection = connectionLookup.get(record.connectionId) ?? null;
      if (record.transfer) {
        items.push({ type: ITEM.transfer, key: `transfer:${record.id}`, group: transferActivityGroup(record.transfer), timestamp: record.occurredAt,
          connection, transfer: record.transfer });
        continue;
      }
      if (record.request && connection) {
        items.push({ type: ITEM.request, key: `request:${record.id}`, group: managedRequestActivityGroup(record.request), timestamp: record.occurredAt,
          connection, request: record.request });
        continue;
      }
      if (record.holding && connection) {
        items.push({ type: ITEM.holding, key: `holding:${record.id}`, group: managedTrackingActivityGroup(record.holding), timestamp: record.occurredAt,
          connection, holding: record.holding });
      }
    }
    return items;
  });
  const attentionItems = $derived(activityItems.filter(item => item.group === REQUEST_ACTIVITY_GROUP.attention));
  const progressItems = $derived(activityItems.filter(item => item.group === REQUEST_ACTIVITY_GROUP.progress));
  const followingItems = $derived(activityItems.filter(item => item.group === REQUEST_ACTIVITY_GROUP.following));
  const recentItems = $derived(activityItems.filter(item => item.group === REQUEST_ACTIVITY_GROUP.recent));
  const visibleRecentItems = $derived(showAllRecent ? recentItems : recentItems.slice(0, RECENT_PREVIEW_COUNT));
  const importedEntityIds = $derived([...new Set(records.flatMap(record => record.transfer?.importedEntityIds ?? []))]);
  const staleSources = $derived(sources.filter(source => source.isStale));
  const sourceWarning = $derived(staleSources.map(source => source.problem ? `${source.name}: ${source.problem}`
    : source.lastCheckedAt ? `${source.name}: last verified ${formatRelativeTime(source.lastCheckedAt)}`
      : `${source.name}: not verified yet`).join(" "));

  $effect(() => {
    const hideNsfw = nsfw.mode !== "show";
    const ids = importedEntityIds;
    const scope = JSON.stringify([hideNsfw, ids]);
    if (scope === importedVisibilityScope) return;
    importedVisibilityScope = scope;
    const sequence = ++importedVisibilitySequence;
    visibleImportedEntityIds = ids.length ? null : new Set();
    if (!ids.length) return;
    void fetchEntityThumbnails(ids, { hideNsfw }).then(items => {
      if (alive && sequence === importedVisibilitySequence) {
        visibleImportedEntityIds = new Set(items.map(item => item.id));
      }
    }).catch(() => {
      // A failed availability check must not turn a transient API error into a hidden-item claim.
      if (alive && sequence === importedVisibilitySequence) visibleImportedEntityIds = null;
    });
  });

  $effect(() => {
    const mode = nsfw.mode;
    if (mode === loadedNsfwMode) return;
    loadedNsfwMode = mode;
    records = [];
    sources = [];
    nextCursor = null;
    showAllRecent = false;
    loading = true;
    void load(true);
  });

  onMount(() => {
    void load(true);
    const timer = setInterval(() => { if (!busyKey && !showAllRecent) void load(); }, 10000);
    return () => { alive = false; clearInterval(timer); };
  });

  async function load(reset = true) {
    const sequence = ++loadSequence;
    if (!reset) loadingMore = true;
    try {
      const page = await fetchRequestActivity({ connectionId: connectionId || undefined, cursor: reset ? undefined : nextCursor ?? undefined,
        limit: PAGE_SIZE, hideNsfw: nsfw.mode !== "show" });
      if (!alive || sequence !== loadSequence) return;
      records = reset ? page.items : mergeRecords(records, page.items);
      sources = page.sources;
      nextCursor = page.nextCursor ?? null;
      error = null;
    } catch (cause) {
      if (alive && sequence === loadSequence) error = message(cause, "Could not refresh request activity");
    } finally {
      if (alive && sequence === loadSequence) {
        loading = false;
        loadingMore = false;
      }
    }
  }

  async function runAction(key: string, action: () => Promise<unknown>) {
    busyKey = key; actionError = null;
    try { await action(); await load(true); }
    catch (cause) { actionError = message(cause, "Could not update this activity"); }
    finally { busyKey = null; }
  }
  async function openEntity(key: string, id: string) {
    busyKey = key; actionError = null;
    try {
      const href = await resolveEntityHrefById(id, { hideNsfw: nsfw.mode !== "show" });
      if (!href) throw new Error("The imported entity does not have a library page yet.");
      await goto(href);
    } catch (cause) { actionError = message(cause, "Could not open the imported item"); }
    finally { busyKey = null; }
  }
  function updateHolding(connectionId: string, saved: ManagedTrackingResponse) {
    records = records.map(record => record.connectionId === connectionId && record.holding?.id === saved.id
      ? { ...record, holding: saved }
      : record);
  }
  function mergeRecords(current: RequestActivityRecord[], next: RequestActivityRecord[]) {
    const merged = new Map(current.map(record => [recordKey(record), record]));
    for (const record of next) merged.set(recordKey(record), record);
    return [...merged.values()];
  }
  function recordKey(record: RequestActivityRecord) {
    if (record.transfer) return `${ITEM.transfer}:${record.id}`;
    if (record.request) return `${ITEM.request}:${record.id}`;
    return `${ITEM.holding}:${record.id}`;
  }
  function selectConnection(value: string) {
    connectionId = value;
    records = [];
    sources = [];
    nextCursor = null;
    showAllRecent = false;
    loading = true;
    void load(true);
  }
  function hasManagerOperation(connection: ConnectionResponse, operation: PluginIntegrationCapabilityOperationsItem) {
    return connection.enabled && connection.enabledCapabilities.includes(PLUGIN_CAPABILITY.externalManager)
      && connection.effectiveCapabilities.some(item => item.kind === PLUGIN_CAPABILITY.externalManager && item.operations.includes(operation));
  }
  function canControl(connection: ConnectionResponse) { return hasManagerOperation(connection, INTEGRATION_OPERATION.reconcileManaged); }
  function canRelease(connection: ConnectionResponse) { return hasManagerOperation(connection, INTEGRATION_OPERATION.inspectManagedRelease); }
  function message(cause: unknown, fallback: string) { return cause instanceof Error ? cause.message : fallback; }

  const requestLabels: Record<ManagedRequestResponse["phase"], string> = {
    [MANAGED_REQUEST_PHASE.pendingCreation]: "Request queued", [MANAGED_REQUEST_PHASE.creationUncertain]: "Check creation",
    [MANAGED_REQUEST_PHASE.awaitingFiles]: "Waiting for files", [MANAGED_REQUEST_PHASE.completed]: "Imported",
    [MANAGED_REQUEST_PHASE.rejected]: "Request refused", [MANAGED_REQUEST_PHASE.cancelled]: "Cancelled",
    [MANAGED_REQUEST_PHASE.ownershipReleased]: "Ownership released",
    [MANAGED_REQUEST_PHASE.remoteRemoved]: "Removed from source",
  };
  const holdingLabels: Record<ManagedTrackingResponse["status"], string> = {
    [MANAGED_TRACKING_STATUS.pending]: "Verifying", [MANAGED_TRACKING_STATUS.waitingForFiles]: "Waiting for files",
    [MANAGED_TRACKING_STATUS.tracking]: "Following", [MANAGED_TRACKING_STATUS.needsReview]: "Review tracking",
    [MANAGED_TRACKING_STATUS.stale]: "Source unavailable", [MANAGED_TRACKING_STATUS.releasePending]: "Handoff pending",
    [MANAGED_TRACKING_STATUS.released]: "Ownership released",
    [MANAGED_TRACKING_STATUS.removed]: "Removed from source",
  };
  const refreshableRequestPhases = new Set<ManagedRequestResponse["phase"]>([
    MANAGED_REQUEST_PHASE.pendingCreation, MANAGED_REQUEST_PHASE.creationUncertain, MANAGED_REQUEST_PHASE.awaitingFiles,
  ]);
  function localAvailability(holding: ManagedTrackingResponse) {
    if (holding.status === MANAGED_TRACKING_STATUS.released) return "Existing file links and history retained";
    const available = holding.bindings.filter(binding => binding.isAvailable).length;
    if (holding.status === MANAGED_TRACKING_STATUS.removed) {
      return available > 0
        ? `${available} linked ${available === 1 ? "file remains" : "files remain"} available locally`
        : "Metadata and request history retained; no local files remain";
    }
    if (!holding.bindings.length) return `${holding.targets.length} remote ${holding.targets.length === 1 ? "item" : "items"} · no local files linked yet`;
    return `${available} of ${holding.bindings.length} linked files available locally`;
  }
</script>

{#snippet activityRow(item: ActivityItem)}
  {@const title = item.type === ITEM.transfer ? item.transfer.title : item.type === ITEM.request ? item.request.title : item.holding.title}
  {@const kind = item.type === ITEM.transfer ? item.transfer.entityKind : item.type === ITEM.holding ? item.holding.item.entityKind : null}
  {@const Icon = kind ? entityKindIcon(kind) : Activity}
  <article class="activity-row" class:expanded={expandedKey === item.key}>
    <div class="activity-icon" aria-hidden="true"><Icon /></div>
    <div class="min-w-0">
      <div class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
        <h4 class="min-w-0 break-words text-sm font-medium text-text-primary">{title}</h4>
        {#if item.type === ITEM.transfer}<Badge variant={item.group === REQUEST_ACTIVITY_GROUP.attention ? "warning" : "default"}>{transferStatusLabel(item.transfer)}</Badge>
        {:else if item.type === ITEM.request}<Badge variant={item.group === REQUEST_ACTIVITY_GROUP.attention ? "warning" : "default"}>{item.group === REQUEST_ACTIVITY_GROUP.attention && item.request.reviewRequired ? "Needs review" : requestLabels[item.request.phase]}</Badge>
        {:else}<Badge variant={item.group === REQUEST_ACTIVITY_GROUP.attention ? "warning" : "default"}>{holdingLabels[item.holding.status]}</Badge>{/if}
      </div>
      <div class="mt-1 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-xs text-text-muted">
        <span>{item.connection?.name ?? "Connected source"}</span><span aria-hidden="true">·</span><span>{formatRelativeTime(item.timestamp)}</span>
        {#if item.type === ITEM.transfer && Number(item.transfer.artifactCount) > 0}<span aria-hidden="true">·</span><span>{item.transfer.artifactCount} {Number(item.transfer.artifactCount) === 1 ? "file" : "files"}</span>{/if}
      </div>
      {#if item.type === ITEM.holding}<p class="mt-1 text-xs text-text-muted">{localAvailability(item.holding)}</p>{/if}
      {#if item.type === ITEM.request && item.request.phase === MANAGED_REQUEST_PHASE.awaitingFiles}<p class="mt-1 text-xs text-text-muted">The remote manager is following this title; Prismedia has not confirmed a local file yet.</p>{/if}
      {#if item.type === ITEM.request && item.request.phase === MANAGED_REQUEST_PHASE.remoteRemoved}<p class="mt-1 text-xs text-text-muted">Metadata and request history retained; this title is no longer waiting for files.</p>{/if}
      {#if item.type === ITEM.transfer && item.transfer.phase === INTEGRATION_TRANSFER_PHASE.completed && item.transfer.importedEntityIds.length}<p class="mt-1 text-xs text-text-muted">Added to your Prismedia library</p>{/if}
      {#if item.type === ITEM.transfer && item.transfer.importedEntityIds.some(id => visibleImportedEntityIds !== null && !visibleImportedEntityIds.has(id))}<p class="mt-1 text-xs text-text-muted">An imported item is unavailable or hidden by your current visibility settings.</p>{/if}
      {#if item.type === ITEM.transfer && item.transfer.mode === INTEGRATION_TRANSFER_MODE.sourceRequest && item.transfer.canCancel}<p class="mt-1 text-xs text-text-muted">Stopping this import leaves the source’s download running.</p>{/if}
      {#if item.type === ITEM.transfer && item.transfer.sourcePublication?.attribution}<div class="mt-1"><SourceAttribution attribution={item.transfer.sourcePublication.attribution} compact /></div>{/if}
      {#if item.type === ITEM.transfer && transferProblem(item.transfer)}<p class="mt-2 break-words text-sm text-text-muted">{transferProblem(item.transfer)}</p>{/if}
      {#if item.type === ITEM.request && item.request.problem}<p class="mt-2 break-words text-sm text-text-muted">{item.request.problem}</p>{/if}
      {#if item.type === ITEM.holding && item.holding.problem}<p class="mt-2 break-words text-sm text-text-muted">{item.holding.problem}</p>{/if}
    </div>
    <div class="activity-actions">
      {#if item.type === ITEM.transfer}
        {#if item.transfer.canCancel}<Button variant="ghost" size="sm" disabled={busyKey !== null} onclick={() => void runAction(item.key, () => cancelPublicationTransfer(item.transfer.id))}><X />{transferCancelLabel(item.transfer.mode)}</Button>{/if}
        {#if transferProblem(item.transfer) && !isTransferTerminal(item.transfer.phase)}<Button variant="secondary" size="sm" disabled={busyKey !== null} onclick={() => void runAction(item.key, () => retryPublicationTransfer(item.transfer.id))}><RotateCcw />{transferRetryLabel(item.transfer)}</Button>{/if}
        {#each item.transfer.importedEntityIds as entityId}
          {@const unavailable = visibleImportedEntityIds !== null && !visibleImportedEntityIds.has(entityId)}
          <Button variant="secondary" size="sm" disabled={busyKey !== null || unavailable} title={unavailable ? "Unavailable or hidden by current visibility settings" : undefined} onclick={() => void openEntity(item.key, entityId)}>{unavailable ? "Unavailable" : "Open"}<ArrowUpRight /></Button>
        {/each}
      {:else if item.type === ITEM.request}
        {#if refreshableRequestPhases.has(item.request.phase)}<Button variant="outline" size="sm" disabled={busyKey !== null} onclick={() => void runAction(item.key, () => refreshRequest(item.connection.id, item.request.id))}><RefreshCw />Refresh</Button>{/if}
        {#if item.request.canCancel}<Button variant="ghost" size="sm" disabled={busyKey !== null} onclick={() => void runAction(item.key, () => cancelRequest(item.connection.id, item.request))}><X />Cancel</Button>{/if}
        {#if item.request.remoteId && item.request.phase !== MANAGED_REQUEST_PHASE.remoteRemoved}<Button variant="ghost" size="sm" aria-expanded={expandedKey === item.key} onclick={() => expandedKey = expandedKey === item.key ? null : item.key}>{expandedKey === item.key ? "Hide controls" : "Manage"}</Button>{/if}
      {:else}
        {#if item.holding.status !== MANAGED_TRACKING_STATUS.released && item.holding.status !== MANAGED_TRACKING_STATUS.removed}<Button variant="outline" size="sm" disabled={busyKey !== null} onclick={() => void runAction(item.key, () => refreshTracking(item.connection.id, item.holding.id))}><RefreshCw />{item.holding.status === MANAGED_TRACKING_STATUS.releasePending ? "Refresh handoff" : "Refresh"}</Button>{/if}
        {#if item.holding.status !== MANAGED_TRACKING_STATUS.released && item.holding.status !== MANAGED_TRACKING_STATUS.removed}<Button variant="ghost" size="sm" aria-expanded={expandedKey === item.key} onclick={() => expandedKey = expandedKey === item.key ? null : item.key}>{expandedKey === item.key ? "Hide controls" : "Manage"}</Button>{/if}
        {#if item.holding.status === MANAGED_TRACKING_STATUS.removed}
          {#each [...new Set(item.holding.targets.map(target => target.entityId))] as entityId}
            <Button variant="secondary" size="sm" disabled={busyKey !== null} onclick={() => void openEntity(item.key, entityId)}>Open<ArrowUpRight /></Button>
          {/each}
        {/if}
      {/if}
    </div>
    {#if expandedKey === item.key && item.type === ITEM.request && item.request.remoteId
      && item.request.phase !== MANAGED_REQUEST_PHASE.remoteRemoved}
      <div class="activity-details"><ManagedHoldingControls connectionId={item.connection.id} connectionName={item.connection.name} holdingId={item.request.holdingId} canPreview={canControl(item.connection) && item.request.phase !== MANAGED_REQUEST_PHASE.ownershipReleased} /></div>
    {:else if expandedKey === item.key && item.type === ITEM.holding
      && item.holding.status !== MANAGED_TRACKING_STATUS.removed}
      <div class="activity-details space-y-3">
        {#if item.holding.lastCheckedAt}<p class="text-xs text-text-muted">Last checked {new Date(item.holding.lastCheckedAt).toLocaleString()}</p>{/if}
        <ManagedHoldingControls connectionId={item.connection.id} connectionName={item.connection.name} holdingId={item.holding.id} canPreview={canControl(item.connection) && (item.holding.status === MANAGED_TRACKING_STATUS.tracking || item.holding.status === MANAGED_TRACKING_STATUS.waitingForFiles)} />
        {#if canRelease(item.connection) && (item.holding.status === MANAGED_TRACKING_STATUS.tracking || item.holding.status === MANAGED_TRACKING_STATUS.waitingForFiles)}<ManagedHoldingRelease connectionId={item.connection.id} connectionName={item.connection.name} holdingId={item.holding.id} onaccepted={saved => updateHolding(item.connection.id, saved)} />{/if}
      </div>
    {/if}
  </article>
{/snippet}

{#snippet activitySection(group: RequestActivityGroup, title: string, description: string, items: ActivityItem[], totalCount = items.length)}
  {#if items.length}
    <section class="activity-section space-y-3" data-group={group} aria-labelledby={`activity-${group}`}>
      <div class="section-heading"><span class="section-mark" aria-hidden="true"></span><div class="min-w-0"><h3 id={`activity-${group}`} class="text-base font-semibold">{title}</h3><p class="mt-0.5 text-sm text-text-muted">{description}</p></div><span class="font-mono text-xs text-text-muted">{items.length === totalCount ? totalCount : `${items.length} of ${totalCount}`}</span></div>
      <Panel class="activity-list overflow-hidden p-0">{#each items as item (item.key)}{@render activityRow(item)}{/each}</Panel>
    </section>
  {/if}
{/snippet}

<div class="space-y-6">
  <div class="flex flex-wrap items-end justify-between gap-3"><div class="space-y-1"><h2 class="text-lg font-semibold">Request activity</h2><p class="text-sm text-text-muted">Review work that needs you, follow active requests, and revisit recent additions.</p></div><a class={buttonVariants({ variant: "ghost", size: "sm" })} href="/downloads">Download queue<ArrowUpRight /></a></div>
  <Panel class="flex-row flex-wrap items-center justify-between gap-3 p-3"><div class="w-full sm:w-72"><Select ariaLabel="Activity source" value={connectionId} options={[{ value: "", label: "All sources" }, ...connections.map(item => ({ value: item.id, label: item.name }))]} onchange={selectConnection} /></div><div class="flex items-center gap-2">{#if showAllRecent}<Button variant="ghost" size="sm" onclick={() => { showAllRecent = false; loading = true; void load(true); }}><RefreshCw />Refresh activity</Button>{/if}{#if !loading}<p class="text-xs text-text-muted">{attentionItems.length + progressItems.length} active · {followingItems.length} followed · {recentItems.length} recent{nextCursor ? "+" : ""}</p>{/if}</div></Panel>
  {#if error}<Alert.Root><AlertTriangle /><Alert.Description>{error} Showing the last activity that is still available.</Alert.Description></Alert.Root>{/if}
  {#if !error && sourceWarning}<Alert.Root><AlertTriangle /><Alert.Description>{sourceWarning} Showing locally retained activity; source status may be stale.</Alert.Description></Alert.Root>{/if}
  {#if actionError}<Alert.Root variant="destructive"><Alert.Description>{actionError}</Alert.Description></Alert.Root>{/if}
  {#if loading}<StatePlaceholder icon={Activity} title="Loading activity" busy />
  {:else if !activityItems.length && error}<StatePlaceholder icon={AlertTriangle} title="Activity is unavailable" description="Prismedia could not confirm whether requests need attention. Try again when the connected sources are available." />
  {:else if !activityItems.length && nextCursor}<div class="space-y-3"><StatePlaceholder icon={Activity} title="More activity is available" description="The current visibility settings hid this page." /><div class="flex justify-center"><Button variant="ghost" size="sm" disabled={loadingMore} onclick={() => void load(false)}><History />Load more activity</Button></div></div>
  {:else if !activityItems.length}<StatePlaceholder icon={Activity} title="Nothing to follow yet" description="Choose a title in Browse to start a request or import." />
  {:else}
    {@render activitySection(REQUEST_ACTIVITY_GROUP.attention, "Needs attention", "Review errors or uncertain outcomes before starting the same work again.", attentionItems)}
    {@render activitySection(REQUEST_ACTIVITY_GROUP.progress, "In progress", "Work still moving through a connected source or into your local library.", progressItems)}
    {#if !error && !sourceWarning && !attentionItems.length && !progressItems.length}<Panel class="flex-row items-center gap-3 px-3 py-2"><div class="caught-up-icon" aria-hidden="true"><CheckCircle2 /></div><p class="text-sm text-text-muted"><strong class="font-medium text-text-primary">All caught up.</strong> No requests need attention and nothing is currently moving.</p></Panel>{/if}
    {@render activitySection(REQUEST_ACTIVITY_GROUP.following, "Following in your library", "These titles remain connected to remote managers; local availability is shown separately.", followingItems)}
    {@render activitySection(REQUEST_ACTIVITY_GROUP.recent, "Recent history", "Completed, cancelled, and released work stays available without crowding active requests.", visibleRecentItems, recentItems.length)}
    {#if recentItems.length > RECENT_PREVIEW_COUNT || nextCursor}<div class="flex justify-center"><Button variant="ghost" size="sm" disabled={busyKey !== null || loadingMore} onclick={() => { if (nextCursor) { showAllRecent = true; void load(false); } else showAllRecent = !showAllRecent; }}><History />{showAllRecent && !nextCursor ? "Show recent preview" : nextCursor ? "Load more activity" : `Show all ${recentItems.length} recent items`}</Button></div>{/if}
  {/if}
</div>

<style>
  .section-heading { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 0.75rem; }
  .section-mark { width: 0.2rem; height: 2rem; border-radius: var(--radius-xs); background: var(--color-border-default); }
  .activity-section[data-group="attention"] .section-mark { background: var(--color-material-orange, #b76337); }
  :global(.activity-list) { background: var(--color-surface-2); }
  .activity-row { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 0.75rem; padding: 0.8rem 0.9rem; border-bottom: 1px solid var(--color-border-subtle); }
  .activity-row:last-child { border-bottom: 0; }
  .activity-icon, .caught-up-icon { display: grid; width: 2rem; height: 2rem; place-items: center; flex: none; border-radius: var(--radius-xs); background: var(--color-surface-3); color: var(--color-text-muted); }
  .activity-icon :global(svg), .caught-up-icon :global(svg) { width: 1rem; height: 1rem; }
  .activity-actions { display: flex; grid-column: 2; flex-wrap: wrap; align-items: flex-start; gap: 0.35rem; }
  .activity-details { grid-column: 2; min-width: 0; padding-top: 0.25rem; }
  .activity-row.expanded { background: var(--color-surface-1); }
  @media (min-width: 768px) { .activity-row { grid-template-columns: auto minmax(0, 1fr) auto; align-items: start; } .activity-actions { grid-column: auto; justify-content: flex-end; } .activity-details { grid-column: 2 / -1; } }
</style>
