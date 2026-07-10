<script lang="ts">
  import { onDestroy } from "svelte";
  import { CloudDownload, FileText, History, Loader2, RefreshCw, Search, SearchX, Upload, X } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import AcquisitionHistoryList from "$lib/components/acquisitions/AcquisitionHistoryList.svelte";
  import PieceStateBar from "$lib/components/acquisitions/PieceStateBar.svelte";
  import ReleaseTable from "$lib/components/acquisitions/ReleaseTable.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { isTransferActive, transferStageLabel } from "$lib/requests/acquisition-transfer";
  import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
  import type {
    AcquisitionDetail,
    AcquisitionFilesView,
    AcquisitionHistoryView,
    AcquisitionTransferView,
    ReleaseCandidateView,
  } from "$lib/api/generated/model";
  import {
    blocklistAcquisitionCandidate,
    cancelAcquisition,
    reSearchAcquisition,
    retryAcquisitionImport,
    fetchAcquisition,
    fetchAcquisitionFiles,
    fetchAcquisitionHistory,
    fetchAcquisitionTransfer,
    queueAcquisitionCandidate,
    uploadManualTorrent,
  } from "$lib/api/acquisitions";
  import {
    ACTIVE_ACQUISITION_STATUSES,
    acquisitionStatusIsKnown,
    acquisitionStatusLabel,
    acquisitionStatusShouldPoll,
  } from "$lib/requests/acquisition-status";
  import { formatBytes, formatEta, formatSpeed } from "$lib/utils/format";

  /**
   * The acquisition-specific management surface: status, live transfer, imported files, release
   * review, search-again, and cancel. Entity monitoring stays in the owning EntityAcquisitionCard so
   * a stable Entity monitor is never duplicated by an acquisition-scoped control.
   */
  let {
    acquisitionId,
    detail = $bindable(null),
    onCancelled,
    onImported,
  }: {
    acquisitionId: string;
    /** The loaded acquisition, bound up so a host page can drive its own hero/badges from it. */
    detail?: AcquisitionDetail | null;
    /**
     * Called after a successful cancel. A wanted entity's page must navigate away here — cancelling
     * a request deletes its wanted placeholder, so the page it sat on no longer exists.
     */
    onCancelled?: () => void;
    /** Called once when live status observes this acquisition cross into Imported. */
    onImported?: () => void | Promise<void>;
  } = $props();

  let transfer = $state<AcquisitionTransferView | null>(null);
  let files = $state<AcquisitionFilesView | null>(null);
  let history = $state<AcquisitionHistoryView[]>([]);
  let error = $state<string | null>(null);
  let busy = $state(false);
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let bridgePolls = $state(0);

  const status = $derived(detail?.summary.status ?? null);
  const hasResumableImport = $derived(detail?.summary.hasResumableImport === true);
  const canRetryImport = $derived(
    status === ACQUISITION_STATUS.manualImportRequired ||
      (status === ACQUISITION_STATUS.failed && hasResumableImport),
  );
  const isActive = $derived(status ? ACTIVE_ACQUISITION_STATUSES.includes(status) : false);
  const transitionLocked = $derived(
    status !== null && (
      status === ACQUISITION_STATUS.stopping ||
      !acquisitionStatusIsKnown(status)
    ),
  );
  const canCancel = $derived(isActive && !transitionLocked);
  const canChoose = $derived(status === ACQUISITION_STATUS.awaitingSelection);
  // A release can still be (re)selected after a failed or cancelled attempt — picking one re-queues it.
  // A manual-import hold (ambiguous payload or a dangerous file) also reopens the picker so the user
  // can block the bad release and grab a different one.
  const canPickRelease = $derived(
      status === ACQUISITION_STATUS.awaitingSelection ||
      (status === ACQUISITION_STATUS.failed && !hasResumableImport) ||
      status === ACQUISITION_STATUS.cancelled ||
      status === ACQUISITION_STATUS.manualImportRequired,
  );
  const isDownloading = $derived(status === ACQUISITION_STATUS.queued || status === ACQUISITION_STATUS.downloading);
  const isDone = $derived(
    status === ACQUISITION_STATUS.downloaded ||
      status === ACQUISITION_STATUS.importing ||
      status === ACQUISITION_STATUS.imported,
  );

  /**
   * The user's manual Files toggle. Once set it wins over the status-derived default, so a poll
   * reassigning `files` can't snap the list shut again; it resets when the imported flag actually
   * transitions, keeping the collapse-once-imported behavior.
   */
  let filesOpen = $state<boolean | null>(null);
  let lastFilesImported: boolean | null = null;
  $effect(() => {
    const imported = files?.imported ?? null;
    if (imported !== lastFilesImported) {
      lastFilesImported = imported;
      filesOpen = null;
    }
  });

  /** True while a load is in flight, so poll ticks never stack behind a slow transfer probe. */
  let loading = false;
  /** Consecutive background-refresh failures; transient blips stay silent, a persistent outage surfaces. */
  let pollFailures = 0;
  /** Guards the owner refresh when a late interval tick or manual load observes Imported again. */
  let importedNotificationSent = false;
  let lastObservedStatus: AcquisitionDetail["summary"]["status"] | null = detail?.summary.status ?? null;

  async function notifyOwnerWhenImported(
    previousStatus: AcquisitionDetail["summary"]["status"] | null,
    nextStatus: AcquisitionDetail["summary"]["status"],
  ) {
    if (importedNotificationSent || nextStatus !== ACQUISITION_STATUS.imported) return;
    if (!previousStatus || !ACTIVE_ACQUISITION_STATUSES.includes(previousStatus)) return;
    importedNotificationSent = true;
    await onImported?.();
  }

  // `detail` is bindable: either this panel's 3-second poll or the owning Entity's shared poll can
  // advance it. Observe the bound value itself so an external Importing/Downloaded → Imported update
  // cannot bypass the in-place page refresh.
  $effect(() => {
    const nextStatus = detail?.summary.status ?? null;
    const previousStatus = lastObservedStatus;
    lastObservedStatus = nextStatus;
    if (nextStatus) void notifyOwnerWhenImported(previousStatus, nextStatus);
  });

  /**
   * Loads the panel state. A background refresh (the 3s poll) must never flash an error banner for a
   * transient network blip — the panel keeps showing the last good data and only surfaces a message
   * once refreshes have failed repeatedly. Foreground loads (first paint, after an action) report
   * failures immediately, because there is nothing good on screen to keep.
   */
  async function load(background = false) {
    if (!acquisitionId || loading) return;
    loading = true;
    try {
      const nextDetail = await fetchAcquisition(acquisitionId);
      detail = nextDetail;
      // Pull the status-appropriate detail.
      if (isDownloading) {
        transfer = await fetchAcquisitionTransfer(acquisitionId);
      } else {
        transfer = null;
      }
      if (isDownloading || isDone) {
        files = await fetchAcquisitionFiles(acquisitionId);
      }
      pollFailures = 0;
      error = null;
    } catch (err) {
      if (background) {
        pollFailures += 1;
        if (pollFailures >= 3) {
          error = "Live updates are failing — retrying in the background.";
        }
      } else {
        error = err instanceof Error ? err.message : "Failed to load acquisition";
      }
    } finally {
      loading = false;
    }
  }

  /**
   * Loads the entity's durable activity log. Secondary surface: a history-load failure must never break
   * the acquisition view, so it silently degrades to whatever is already shown. Scoped by entity id when
   * the acquisition targets one, so the section shows every grab/import/failure for that wanted item —
   * including events from acquisitions that were since removed.
   */
  async function loadHistory(entityId: string) {
    history = await fetchAcquisitionHistory({ entityId, limit: 50 }).catch(() => history);
  }

  async function queue(candidate: ReleaseCandidateView) {
    if (busy) return;
    busy = true;
    try {
      detail = await queueAcquisitionCandidate(acquisitionId, candidate.id);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to queue release";
    } finally {
      busy = false;
    }
  }

  async function blocklist(candidate: ReleaseCandidateView) {
    if (busy) return;
    busy = true;
    try {
      detail = await blocklistAcquisitionCandidate(acquisitionId, candidate.id);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to blocklist release";
    } finally {
      busy = false;
    }
  }

  async function cancel() {
    if (busy) return;
    busy = true;
    try {
      detail = await cancelAcquisition(acquisitionId);
      onCancelled?.();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to cancel";
    } finally {
      busy = false;
    }
  }

  // Re-run the release search on demand (manual counterpart to monitoring).
  async function reSearch() {
    if (busy) return;
    busy = true;
    try {
      detail = await reSearchAcquisition(acquisitionId);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to re-search";
    } finally {
      busy = false;
    }
  }

  // Re-run a held import. A manual hold carries explicit format-change consent; a failed durable
  // checkpoint simply resumes its already-persisted plan without broadening that consent.
  async function retryImport(allowFormatChange: boolean) {
    if (busy) return;
    busy = true;
    try {
      detail = await retryAcquisitionImport(acquisitionId, allowFormatChange);
      bridgePolls = 8; // bridge-poll so the importing → imported transition lands without a refresh
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to import";
    } finally {
      busy = false;
    }
  }

  async function onUpload(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || busy) return;
    busy = true;
    try {
      detail = await uploadManualTorrent(acquisitionId, file);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to upload torrent";
    } finally {
      busy = false;
      input.value = "";
    }
  }

  // Re-search is offered whenever the item is still seeking a release — including a manual-import
  // hold, where searching for a different release is a legitimate way out. In-flight grabs and
  // imported/cancelled items are left alone (the server enforces the same gate).
  const canReSearch = $derived(
    status === ACQUISITION_STATUS.awaitingSelection ||
      (status === ACQUISITION_STATUS.failed && !hasResumableImport) ||
      status === ACQUISITION_STATUS.manualImportRequired,
  );

  // Search again now returns Searching immediately and uses the ordinary active-status poll. Manual import
  // keeps a short bridge window because the import may finish between the request and the first active tick.
  const shouldPoll = $derived(acquisitionStatusShouldPoll(status) || bridgePolls > 0);

  async function pollTick() {
    if (bridgePolls > 0) bridgePolls -= 1;
    await load(true);
  }

  $effect(() => {
    if (acquisitionId) void load();
  });
  // Load the durable activity log once the acquisition (and thus its entity id) is known. Re-runs when the
  // entity changes, and after the poll advances the status (a fresh grab/import lands a new event).
  $effect(() => {
    const entityId = detail?.summary.entityId;
    if (entityId) void loadHistory(entityId);
  });
  $effect(() => {
    if (shouldPoll && !pollTimer) {
      pollTimer = setInterval(pollTick, 3000);
    } else if (!shouldPoll && pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  });
  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });
</script>

{#if error}
  <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">{error}</div>
{/if}

{#if !detail}
  <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
    <Loader2 class="h-4 w-4 animate-spin" />
    <span class="text-sm">Loading…</span>
  </div>
{:else}
  <div class="space-y-4">
    <!-- ── Status + actions ── -->
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex min-w-0 flex-wrap items-center gap-2.5">
        <Badge variant={status === ACQUISITION_STATUS.imported ? "success" : status === ACQUISITION_STATUS.failed ? "error" : "accent"}>
          {acquisitionStatusLabel(detail.summary.status)}
        </Badge>
        {#if detail.summary.statusMessage}
          <span class="text-sm text-text-muted">{detail.summary.statusMessage}</span>
        {/if}
      </div>
      <div class="flex shrink-0 flex-wrap items-center gap-2">
        {#if canRetryImport}
          <Button
            type="button"
            variant="primary"
            class="gap-1.5"
            disabled={busy}
            onclick={() => void retryImport(status === ACQUISITION_STATUS.manualImportRequired)}
            title={status === ACQUISITION_STATUS.manualImportRequired
              ? "Import the downloaded files now. A genuine upgrade may replace the existing file even when the format differs; the previous file is kept recoverable."
              : "Resume the exact durable import plan from its last completed file."}
          >
            <CloudDownload class="h-3.5 w-3.5" />
            {status === ACQUISITION_STATUS.manualImportRequired ? "Import anyway" : "Retry import"}
          </Button>
        {/if}
        {#if canReSearch}
          <Button type="button" variant="ghost" class="gap-1.5" disabled={busy} onclick={() => void reSearch()}>
            <RefreshCw class="h-3.5 w-3.5" />
            Search again
          </Button>
        {/if}
        {#if canCancel || canChoose}
          <Button type="button" variant="danger" class="gap-1.5" disabled={busy} onclick={() => void cancel()}>
            <X class="h-3.5 w-3.5" />
            Cancel
          </Button>
        {/if}
      </div>
    </div>

    {#if transitionLocked}
      <StatePlaceholder
        icon={Loader2}
        title={status === ACQUISITION_STATUS.stopping ? "Cleaning up acquisition" : "Updating acquisition"}
        description={status === ACQUISITION_STATUS.stopping
          ? "Removing the download and managed files. Actions will return when cleanup finishes."
          : "Prismedia is finishing a newer lifecycle transition. Actions are temporarily unavailable."}
        busy
      />

    {:else if status === ACQUISITION_STATUS.searching}
      <StatePlaceholder
        icon={Search}
        title="Searching indexers"
        description="Querying your configured indexers for matching releases. This can take a moment."
        busy
      />

    {:else if isDownloading}
      <!-- ── Live transfer ── -->
      <section class="space-y-3">
        <h2 class="text-kicker text-text-primary">Download</h2>
        {#if transfer}
          {@const pct = Math.round(Number(transfer.progress) * 100)}
          <div class="space-y-3 rounded-sm border border-border-subtle bg-surface-1 p-3.5">
            <!-- Stage + percent -->
            <div class="flex items-center justify-between gap-3">
              <span class="flex items-center gap-2 text-sm font-medium text-text-primary">
                {#if isTransferActive(transfer.state)}
                  <Loader2 class="h-3.5 w-3.5 animate-spin text-text-accent" />
                {/if}
                {transferStageLabel(transfer.state)}
              </span>
              <span class="font-mono text-sm text-text-accent">{pct}%</span>
            </div>
            <!-- Progress bar -->
            <div class="h-2 w-full overflow-hidden rounded-full bg-surface-3">
              <div class="h-full rounded-full bg-accent-500 transition-all" style:width={`${pct}%`}></div>
            </div>
            <!-- Stats -->
            <div class="grid grid-cols-2 gap-x-4 gap-y-2.5 sm:grid-cols-4">
              {@render stat("Speed", formatSpeed(Number(transfer.downloadSpeedBytesPerSecond)))}
              {@render stat("ETA", formatEta(Number(transfer.etaSeconds)))}
              {@render stat("Seeds / Peers", `${transfer.seeds} / ${transfer.peers}`)}
              {@render stat("Size", formatBytes(Number(transfer.totalSizeBytes)))}
            </div>
            <PieceStateBar pieces={transfer.pieceStates.map(Number)} />
          </div>
        {:else}
          <StatePlaceholder
            icon={CloudDownload}
            title="Preparing download"
            description="Connecting to the download client and waiting for the first progress report…"
            busy
          />
        {/if}
      </section>

    {:else if isDone}
      <!-- ── Imported / downloaded files — collapsed once imported so a big pack doesn't fill the page ── -->
      {#if files && files.files.length > 0}
        <details
          class="group min-w-0 overflow-hidden rounded-sm border border-border-subtle bg-surface-1"
          open={filesOpen ?? !files.imported}
          ontoggle={(event) => (filesOpen = event.currentTarget.open)}
        >
          <summary class="flex min-w-0 cursor-pointer items-center gap-2 px-3 py-2 text-kicker text-text-primary select-none">
            <FileText class="h-3.5 w-3.5 text-text-muted" />
            Files
            <span class="font-mono text-[0.68rem] font-normal text-text-muted">{files.files.length}</span>
          </summary>
          <div class="min-w-0 px-3 pb-3">
            <div class="overflow-hidden rounded-sm border border-border-subtle">
              {#each files.files as f (f.name)}
                <div class="flex items-center justify-between gap-3 border-b border-border-subtle px-3 py-2 last:border-b-0">
                  <span class="flex min-w-0 items-center gap-2 text-sm text-text-primary">
                    <FileText class="h-3.5 w-3.5 shrink-0 text-text-muted" />
                    <span class="truncate">{f.name}</span>
                  </span>
                  <span class="shrink-0 font-mono text-[0.72rem] text-text-muted">{formatBytes(Number(f.sizeBytes))}</span>
                </div>
              {/each}
            </div>
          </div>
        </details>
      {:else}
        <section class="space-y-2">
          <h2 class="text-kicker text-text-primary">Files</h2>
          <StatePlaceholder
            icon={FileText}
            title="No files yet"
            description="Files will appear here once the download produces them."
            busy={isActive}
          />
        </section>
      {/if}

    {:else}
      <!-- ── Release review (awaiting selection / failed) ── -->
      <section class="space-y-3">
        <h2 class="text-kicker text-text-primary">
          Releases
          <span class="ml-1.5 font-mono text-[0.68rem] font-normal text-text-muted">{detail.candidates.length}</span>
        </h2>

        {#if detail.candidates.length === 0}
          <StatePlaceholder
            icon={SearchX}
            title="No releases found"
            description="No indexer returned a matching release for this title. You can upload a .torrent manually below."
          />
        {:else}
          <ReleaseTable candidates={detail.candidates} canChoose={canPickRelease} {busy} onQueue={queue} onBlocklist={blocklist} />
        {/if}

        {#if canPickRelease}
          <!-- ── Manual .torrent fallback ── -->
          <div class="flex flex-wrap items-center gap-3 rounded-sm border border-dashed border-border-subtle bg-surface-1 p-3">
            <div class="min-w-0 flex-1">
              <p class="text-sm font-medium text-text-primary">Have a .torrent file?</p>
              <p class="text-[0.72rem] text-text-muted">Open a release page above, download its .torrent, then upload it here to download directly.</p>
            </div>
            <label class="inline-flex cursor-pointer items-center gap-1.5 rounded-xs border border-border-subtle bg-surface-2 px-3 py-1.5 text-[0.75rem] font-medium text-text-secondary transition-colors hover:text-text-primary">
              <Upload class="h-3.5 w-3.5" />
              Upload .torrent
              <input type="file" accept=".torrent,application/x-bittorrent" class="hidden" onchange={onUpload} disabled={busy} />
            </label>
          </div>
        {/if}
      </section>
    {/if}

    <!-- ── History (durable activity log for this item) ── -->
    {#if history.length > 0}
      <details class="group min-w-0 overflow-hidden rounded-sm border border-border-subtle bg-surface-1">
        <summary class="flex min-w-0 cursor-pointer items-center gap-2 px-3 py-2 text-kicker text-text-primary select-none">
          <History class="h-3.5 w-3.5 text-text-muted" />
          History
          <span class="font-mono text-[0.68rem] font-normal text-text-muted">{history.length}</span>
        </summary>
        <div class="min-w-0 px-3 pb-3">
          <AcquisitionHistoryList entries={history} />
        </div>
      </details>
    {/if}
  </div>
{/if}

{#snippet stat(label: string, value: string)}
  <div class="flex flex-col gap-0.5">
    <span class="text-label text-text-muted">{label}</span>
    <span class="font-mono text-[0.8rem] text-text-primary">{value}</span>
  </div>
{/snippet}
