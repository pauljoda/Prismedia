<script lang="ts">
  import { onDestroy, onMount } from "svelte";
  import { CalendarClock, CircleAlert, CircleCheck, CloudDownload, FileText, History, Loader2, PencilLine, RefreshCw, RotateCcw, Search, SearchX, Upload, X } from "@lucide/svelte";
  import { Alert, Badge, Button, Card, Disclosure, SearchInput, type BadgeVariant } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import type { PathnameWithSearchOrHash } from "$app/types";
  import AcquisitionHistoryList from "$lib/components/acquisitions/AcquisitionHistoryList.svelte";
  import ManualImportReview from "$lib/components/acquisitions/ManualImportReview.svelte";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";
  import AcquisitionTransferSummary from "$lib/components/acquisitions/AcquisitionTransferSummary.svelte";
  import ReleaseTable from "$lib/components/acquisitions/ReleaseTable.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { presentAcquisitionTransfer } from "$lib/requests/acquisition-transfer-presentation";
  import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
  import { METADATA_PATCH_FIELD } from "$lib/entities/entity-codes";
  import type {
    AcquisitionDetail,
    AcquisitionFilesView,
    AcquisitionHistoryView,
    AcquisitionManualImportReview,
    AcquisitionManualImportSelection,
    AcquisitionTransferView,
    ReleaseCandidateView,
  } from "$lib/api/generated/model";
  import {
    blocklistAcquisitionCandidate,
    cancelAcquisition,
    deleteAcquisition,
    reSearchAcquisition,
    retryAcquisitionImport,
    fetchAcquisition,
    fetchAcquisitionFiles,
    fetchAcquisitionHistory,
    fetchAcquisitionManualImportReview,
    fetchAcquisitionTransfer,
    queueAcquisitionCandidate,
    rejectAcquisitionManualImport,
    submitAcquisitionManualImport,
    uploadManualTorrent,
  } from "$lib/api/acquisitions";
  import {
    ACTIVE_ACQUISITION_STATUSES,
    acquisitionStatusIsKnown,
    acquisitionStatusLabel,
    acquisitionStatusShouldPoll,
  } from "$lib/requests/acquisition-status";
  import { formatBytes } from "$lib/utils/format";

  /**
   * The acquisition-specific management surface: status, live transfer, imported files, release
   * review, search-again, and cancel. Entity monitoring stays in the owning EntityAcquisitionCard so
   * a stable Entity monitor is never duplicated by an acquisition-scoped control.
   */
  let {
    acquisitionId,
    detail = null,
    onDetailChange,
    onCancelled,
    onImported,
    onReset,
  }: {
    acquisitionId: string;
    /** The latest acquisition state supplied by the owning page controller. */
    detail?: AcquisitionDetail | null;
    /** Publishes panel loads and mutations without mutating the owner's state through a bound prop. */
    onDetailChange?: (detail: AcquisitionDetail | null) => void;
    /**
     * Called after a successful cancel. A wanted entity's page must navigate away here — cancelling
     * a request deletes its wanted placeholder, so the page it sat on no longer exists.
     */
    onCancelled?: () => void;
    /** Called once when live status observes this acquisition cross into Imported. */
    onImported?: () => void | Promise<void>;
    /** Reloads the owning Entity after the old acquisition is replaced with a clean search. */
    onReset?: () => void | Promise<void>;
  } = $props();

  let transfer = $state<AcquisitionTransferView | null>(null);
  let files = $state<AcquisitionFilesView | null>(null);
  let history = $state<AcquisitionHistoryView[]>([]);
  let manualImportReview = $state<AcquisitionManualImportReview | null>(null);
  let manualAssignments = $state<Record<string, string>>({});
  let error = $state<string | null>(null);
  let busy = $state(false);
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let bridgePolls = $state(0);
  let resetConfirmOpen = $state(false);
  let rejectConfirmOpen = $state(false);
  let unsafeImportConfirmOpen = $state(false);
  let overrideCandidate = $state<ReleaseCandidateView | null>(null);
  let customQuery = $state("");
  let lastHistoryKey: string | null = null;

  const EDIT_QUERY_KEY = "edit";

  const status = $derived(detail?.summary.status ?? null);
  const manualImportWarning = $derived(manualImportReview?.warning ?? null);
  const releaseDateMetadataUnavailable = $derived(
    detail?.summary.releaseDateMetadataUnavailable === true ||
      status === ACQUISITION_STATUS.manualSearchRequired,
  );
  const hasResumableImport = $derived(detail?.summary.hasResumableImport === true);
  const canRetryImport = $derived(
    status === ACQUISITION_STATUS.downloaded ||
      (hasResumableImport && (
        status === ACQUISITION_STATUS.awaitingSelection ||
        status === ACQUISITION_STATUS.failed ||
        status === ACQUISITION_STATUS.cancelled
      )),
  );
  const canStartOver = $derived(hasResumableImport && status !== ACQUISITION_STATUS.stopping);
  const isActive = $derived(status ? ACTIVE_ACQUISITION_STATUSES.includes(status) : false);
  const transitionLocked = $derived(
    status !== null && (
      status === ACQUISITION_STATUS.stopping ||
      !acquisitionStatusIsKnown(status)
    ),
  );
  const canCancel = $derived(
    (isActive || status === ACQUISITION_STATUS.awaitingSelection)
      && !transitionLocked
      && !hasResumableImport,
  );
  // A release can still be (re)selected after a failed or cancelled attempt — picking one re-queues it.
  // A manual-import hold (ambiguous payload or a dangerous file) also reopens the picker so the user
  // can block the bad release and grab a different one.
  const canPickRelease = $derived(
    status === ACQUISITION_STATUS.awaitingSelection ||
      status === ACQUISITION_STATUS.failed ||
      status === ACQUISITION_STATUS.cancelled ||
      status === ACQUISITION_STATUS.manualImportRequired,
  );
  const canSearchReleases = $derived(canPickRelease && !hasResumableImport);
  const isDownloading = $derived(status === ACQUISITION_STATUS.queued || status === ACQUISITION_STATUS.downloading);
  const isDone = $derived(
    status === ACQUISITION_STATUS.downloaded ||
      status === ACQUISITION_STATUS.importing ||
      status === ACQUISITION_STATUS.imported,
  );
  const panelTitle = $derived(panelTitleFor(status));
  const panelDescription = $derived(
    status === ACQUISITION_STATUS.manualImportRequired
      ? "Prismedia stopped before adding these files to your library."
      : null,
  );

  function panelTitleFor(value: AcquisitionDetail["summary"]["status"] | null): string {
    if (value === ACQUISITION_STATUS.manualImportRequired) return "Import blocked";
    if (value === ACQUISITION_STATUS.awaitingSelection) return "Choose a release";
    if (value === ACQUISITION_STATUS.searching) return "Searching releases";
    if (value === ACQUISITION_STATUS.queued || value === ACQUISITION_STATUS.downloading) return "Downloading release";
    if (value === ACQUISITION_STATUS.waitingForDownloadClient) return "Download paused";
    if (value === ACQUISITION_STATUS.downloaded || value === ACQUISITION_STATUS.importing) return "Adding to your library";
    if (value === ACQUISITION_STATUS.imported) return "In your library";
    if (value === ACQUISITION_STATUS.failed) return "Acquisition failed";
    if (value === ACQUISITION_STATUS.cancelled) return "Acquisition cancelled";
    if (value === ACQUISITION_STATUS.stopping) return "Cleaning up acquisition";
    if (value === ACQUISITION_STATUS.waitingForRelease || value === ACQUISITION_STATUS.manualSearchRequired) return "Waiting for release";
    return value ? acquisitionStatusLabel(value) : "Acquisition";
  }

  function statusBadgeVariant(value: AcquisitionDetail["summary"]["status"]): BadgeVariant {
    if (value === ACQUISITION_STATUS.imported) return "success";
    if (value === ACQUISITION_STATUS.failed || value === ACQUISITION_STATUS.cancelled) return "error";
    if (value === ACQUISITION_STATUS.manualImportRequired) return "warning";
    if (acquisitionStatusShouldPoll(value)) return "info";
    return "default";
  }

  function statusBadgeLabel(value: AcquisitionDetail["summary"]["status"]): string {
    if (value === ACQUISITION_STATUS.manualImportRequired) return "Review required";
    if (value === ACQUISITION_STATUS.waitingForRelease) return "Scheduled";
    if (value === ACQUISITION_STATUS.manualSearchRequired) return "Date needed";
    if (value === ACQUISITION_STATUS.searching || value === ACQUISITION_STATUS.stopping) return "In progress";
    return acquisitionStatusLabel(value);
  }

  /** The user's Files disclosure choice, scoped to the imported state that produced it. */
  let filesOpenPreference = $state<{ imported: boolean | null; open: boolean } | null>(null);

  function filesDisclosureOpen(): boolean {
    const imported = files?.imported ?? null;
    return filesOpenPreference?.imported === imported
      ? filesOpenPreference.open
      : !Boolean(imported);
  }

  /** True while a load is in flight, so poll ticks never stack behind a slow transfer probe. */
  let loading = false;
  /** Consecutive background-refresh failures; transient blips stay silent, a persistent outage surfaces. */
  let pollFailures = 0;
  /** Guards the owner refresh when a late interval tick or manual load observes Imported again. */
  let importedNotificationSent = false;
  let statusObservationInitialized = false;
  let lastObservedStatus: AcquisitionDetail["summary"]["status"] | null = null;

  function commitDetail(nextDetail: AcquisitionDetail | null) {
    detail = nextDetail;
    onDetailChange?.(nextDetail);
  }

  async function notifyOwnerWhenImported(
    previousStatus: AcquisitionDetail["summary"]["status"] | null,
    nextStatus: AcquisitionDetail["summary"]["status"],
  ) {
    if (importedNotificationSent || nextStatus !== ACQUISITION_STATUS.imported) return;
    if (!previousStatus || !ACTIVE_ACQUISITION_STATUSES.includes(previousStatus)) return;
    importedNotificationSent = true;
    await onImported?.();
  }

  // Either this panel's 3-second poll or the owning Entity's shared poll can advance `detail`.
  // Observe the prop value itself so an external Importing/Downloaded → Imported update
  // cannot bypass the in-place page refresh.
  $effect(() => {
    const nextStatus = detail?.summary.status ?? null;
    if (!statusObservationInitialized) {
      statusObservationInitialized = true;
      lastObservedStatus = nextStatus;
      return;
    }
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
      commitDetail(nextDetail);
      // Pull the status-appropriate detail.
      if (isDownloading) {
        transfer = await fetchAcquisitionTransfer(acquisitionId);
      } else {
        transfer = null;
      }
      if (isDownloading || isDone) {
        files = await fetchAcquisitionFiles(acquisitionId);
      }
      if (status === ACQUISITION_STATUS.manualImportRequired) {
        const nextReview = await fetchAcquisitionManualImportReview(acquisitionId);
        manualImportReview = nextReview;
        const allowedSources = new Set(nextReview.files
          .filter((file) => file.canMap)
          .map((file) => file.sourceRelativePath));
        const suggestedSourceByTarget = new Map(nextReview.files
          .filter((file) => file.suggestedTargetEntityId && file.canMap)
          .map((file) => [file.suggestedTargetEntityId!, file.sourceRelativePath]));
        manualAssignments = Object.fromEntries(nextReview.targets.map((target) => {
          const current = manualAssignments[target.entityId];
          const source = current && allowedSources.has(current)
            ? current
            : suggestedSourceByTarget.get(target.entityId) ?? "";
          return [target.entityId, source];
        }));
      } else {
        manualImportReview = null;
        manualAssignments = {};
      }
      pollFailures = 0;
      if (!background) error = null;
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
  async function loadHistory(nextDetail: AcquisitionDetail) {
    const entityId = nextDetail.summary.entityId;
    if (!entityId) return;
    const historyKey = `${entityId}:${nextDetail.summary.updatedAt}`;
    if (historyKey === lastHistoryKey) return;
    lastHistoryKey = historyKey;
    try {
      history = await fetchAcquisitionHistory({ entityId, limit: 50 });
    } catch {
      // Let the next genuine detail refresh retry a transient secondary-history failure.
      if (lastHistoryKey === historyKey) lastHistoryKey = null;
    }
  }

  async function queue(candidate: ReleaseCandidateView) {
    if (hasResumableImport) {
      overrideCandidate = candidate;
      return;
    }

    await queueCandidate(candidate);
  }

  async function queueCandidate(candidate: ReleaseCandidateView, rethrowOnFailure = false) {
    await runDetailAction(
      () => queueAcquisitionCandidate(acquisitionId, candidate.id),
      "Failed to queue release",
      { refreshOnFailure: true, rethrowOnFailure },
    );
  }

  async function confirmReleaseOverride() {
    if (!overrideCandidate) return;
    await queueCandidate(overrideCandidate, true);
  }

  async function blocklist(candidate: ReleaseCandidateView) {
    await runDetailAction(
      () => blocklistAcquisitionCandidate(acquisitionId, candidate.id),
      "Failed to blocklist release",
    );
  }

  async function cancel() {
    await runDetailAction(
      () => cancelAcquisition(acquisitionId),
      "Failed to cancel",
      { afterSuccess: onCancelled },
    );
  }

  // Re-run the release search on demand (manual counterpart to monitoring).
  async function reSearch(query?: string) {
    await runDetailAction(
      () => reSearchAcquisition(acquisitionId, query),
      "Failed to re-search",
    );
  }

  function openDateEditor() {
    const target = new URL(page.url);
    target.searchParams.set(EDIT_QUERY_KEY, METADATA_PATCH_FIELD.dates);
    target.hash = "entity-dates-editor";
    const targetPath = `${target.pathname}${target.search}${target.hash}` as PathnameWithSearchOrHash;
    // Every member of PathnameWithSearchOrHash is a valid one-argument resolve call. Adapt the
    // generated generic overload so TypeScript accepts the runtime union of current route paths.
    void goto((resolve as (path: PathnameWithSearchOrHash) => string)(targetPath));
  }

  // Re-run a held import. A manual hold carries explicit format-change consent; a failed durable
  // checkpoint simply resumes its already-persisted plan without broadening that consent.
  async function retryImport(allowFormatChange: boolean) {
    await runDetailAction(
      () => retryAcquisitionImport(acquisitionId, allowFormatChange),
      "Failed to import",
      {
        // Bridge-poll so the importing → imported transition lands without a refresh.
        afterSuccess: () => {
          bridgePolls = 8;
        },
      },
    );
  }

  async function importMappedFiles() {
    const selections: AcquisitionManualImportSelection[] = Object.entries(manualAssignments)
      .filter((entry): entry is [string, string] => Boolean(entry[1]))
      .map(([targetEntityId, sourceRelativePath]) => ({ sourceRelativePath, targetEntityId }));
    await runDetailAction(
      () => submitAcquisitionManualImport(acquisitionId, selections),
      "Failed to import mapped files",
      { afterSuccess: () => { bridgePolls = 8; } },
    );
  }

  function requestMappedImport() {
    if (manualImportWarning) {
      unsafeImportConfirmOpen = true;
      return;
    }

    void importMappedFiles();
  }

  async function rejectManualImport() {
    if (busy) return;
    busy = true;
    error = null;
    try {
      await rejectAcquisitionManualImport(acquisitionId);
      commitDetail(null);
      await onReset?.();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to reject downloaded release";
      throw err;
    } finally {
      busy = false;
    }
  }

  async function runDetailAction(
    action: () => Promise<AcquisitionDetail>,
    fallbackError: string,
    options: {
      afterSuccess?: () => void | Promise<void>;
      refreshOnFailure?: boolean;
      rethrowOnFailure?: boolean;
    } = {},
  ) {
    if (busy) return;
    busy = true;
    error = null;
    try {
      commitDetail(await action());
      await options.afterSuccess?.();
    } catch (err) {
      error = err instanceof Error ? err.message : fallbackError;
      // A failed remote-client handoff can still change durable server state. Re-read it while
      // retaining the actionable error instead of leaving a stale card or flashing the error away.
      if (options.refreshOnFailure) await load(true);
      if (options.rethrowOnFailure) throw err;
    } finally {
      busy = false;
    }
  }

  async function startOver() {
    if (busy) return;
    busy = true;
    error = null;
    try {
      await deleteAcquisition(acquisitionId);
      commitDetail(null);
      await onReset?.();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to start acquisition over";
      throw err;
    } finally {
      busy = false;
    }
  }

  async function onUpload(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || busy) return;
    await runDetailAction(
      () => uploadManualTorrent(acquisitionId, file),
      "Failed to upload torrent",
    );
    input.value = "";
  }

  // Waiting-for-release renders its own explicit Manual search action. The toolbar action remains for
  // review, failed-search, and manual-import states where searching for another release is a way out.
  const canReSearch = $derived(
    (status === ACQUISITION_STATUS.awaitingSelection && !hasResumableImport) ||
      (status === ACQUISITION_STATUS.failed && !hasResumableImport),
  );

  // Search again now returns Searching immediately and uses the ordinary active-status poll. Manual import
  // keeps a short bridge window because the import may finish between the request and the first active tick.
  const shouldPoll = $derived(acquisitionStatusShouldPoll(status) || bridgePolls > 0);

  async function pollTick() {
    if (bridgePolls > 0) bridgePolls -= 1;
    await load(true);
  }

  // Every production owner keys this component by acquisition id. Load once for that mounted
  // identity; publishing the refreshed detail back to the owner must not feed another load.
  onMount(() => {
    if (acquisitionId) void load();
  });
  // Load durable activity once per server revision. Parent-owned detail publication replaces the
  // object even when its revision is unchanged, so object identity must never drive this request.
  $effect(() => {
    const nextDetail = detail;
    if (nextDetail) void loadHistory(nextDetail);
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
  <Alert.Root variant="destructive">
    <CircleAlert />
    <Alert.Title>Acquisition could not be updated</Alert.Title>
    <Alert.Description>{error}</Alert.Description>
  </Alert.Root>
{/if}

{#if !detail}
  <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
    <Loader2 class="h-4 w-4 animate-spin" />
    <span class="text-sm">Loading…</span>
  </div>
{:else}
  <Card.Root class={status === ACQUISITION_STATUS.manualImportRequired ? "border-warning/30" : undefined}>
    <Card.Header class="border-b border-border-subtle">
      <Card.Title role="heading" aria-level={2} class="flex items-center gap-2 text-lg">
        {#if status === ACQUISITION_STATUS.manualImportRequired || status === ACQUISITION_STATUS.failed}
          <CircleAlert class="text-warning-text" aria-hidden="true" />
        {:else if status === ACQUISITION_STATUS.imported}
          <CircleCheck class="text-success-text" aria-hidden="true" />
        {:else if isDownloading || isDone}
          <CloudDownload class="text-info-text" aria-hidden="true" />
        {:else if status === ACQUISITION_STATUS.waitingForRelease || status === ACQUISITION_STATUS.manualSearchRequired}
          <CalendarClock class="text-muted-foreground" aria-hidden="true" />
        {:else}
          <Search class="text-muted-foreground" aria-hidden="true" />
        {/if}
        {panelTitle}
      </Card.Title>
      {#if panelDescription}
        <Card.Description>{panelDescription}</Card.Description>
      {/if}
      <Card.Action>
        <Badge variant={statusBadgeVariant(detail.summary.status)}>
          {statusBadgeLabel(detail.summary.status)}
        </Badge>
      </Card.Action>
    </Card.Header>

    <Card.Content class="flex min-w-0 flex-col gap-5">
      {#if transitionLocked}
      <StatePlaceholder
        icon={Loader2}
        title={status === ACQUISITION_STATUS.stopping ? "Cleaning up acquisition" : "Updating acquisition"}
        description={status === ACQUISITION_STATUS.stopping
          ? "Removing the download and managed files. Actions will return when cleanup finishes."
          : "Prismedia is finishing a newer lifecycle transition. Actions are temporarily unavailable."}
        busy
      />
      {:else if status === ACQUISITION_STATUS.waitingForRelease || status === ACQUISITION_STATUS.manualSearchRequired}
      <StatePlaceholder
        icon={CalendarClock}
        title="Waiting for release"
        description={detail.summary.statusMessage ?? "Automatic searches will begin when the configured release milestone arrives. You can still search manually now."}
      >
        <div class="flex flex-wrap items-center justify-center gap-2">
          <Button type="button" variant="secondary" class="gap-1.5" disabled={busy} onclick={() => void reSearch()}>
            <Search class="h-3.5 w-3.5" />
            Manual search
          </Button>
          {#if releaseDateMetadataUnavailable}
            <Button type="button" variant="secondary" class="gap-1.5" onclick={openDateEditor}>
              <PencilLine class="h-3.5 w-3.5" />
              Enter release date
            </Button>
          {/if}
        </div>
      </StatePlaceholder>
      {:else if status === ACQUISITION_STATUS.searching}
      <StatePlaceholder
        icon={Search}
        title="Searching indexers"
        description="Querying your configured indexers for matching releases. This can take a moment."
        busy
      />

      {:else if status === ACQUISITION_STATUS.waitingForDownloadClient}
      <StatePlaceholder
        icon={CloudDownload}
        title="Waiting for download client"
        description={detail?.summary.statusMessage ?? "Prismedia will resume automatically when the download client is healthy."}
        busy
      />

      {:else if status === ACQUISITION_STATUS.manualImportRequired}
      <ManualImportReview
        review={manualImportReview}
        statusMessage={detail.summary.statusMessage}
        assignments={manualAssignments}
        {busy}
        onAssignmentChange={(targetEntityId, sourceRelativePath) => {
          manualAssignments[targetEntityId] = sourceRelativePath;
        }}
        onImport={requestMappedImport}
        onReject={() => (rejectConfirmOpen = true)}
      />

      {:else if isDownloading}
      <AcquisitionTransferSummary transfer={presentAcquisitionTransfer(transfer)} />

      {:else if isDone}
      <!-- ── Imported / downloaded files — collapsed once imported so a big pack doesn't fill the page ── -->
      {#if files && files.files.length > 0}
        <Disclosure
          title="Files"
          icon={FileText}
          count={files.files.length}
          bind:open={() => filesDisclosureOpen(), (next) => (filesOpenPreference = {
            imported: files?.imported ?? null,
            open: next,
          })}
        >
          <div class="min-w-0">
            <div class="overflow-hidden rounded-sm border border-border-subtle">
              {#each files.files as f (f.name)}
                <div class="flex min-w-0 items-start justify-between gap-3 border-b border-border-subtle px-3 py-2 last:border-b-0">
                  <span class="flex min-w-0 items-center gap-2 text-sm text-text-primary">
                    <FileText class="h-3.5 w-3.5 shrink-0 text-text-muted" />
                    <span class="min-w-0 whitespace-normal [overflow-wrap:anywhere]">{f.name}</span>
                  </span>
                  <span class="shrink-0 font-mono text-[0.72rem] text-text-muted">{formatBytes(Number(f.sizeBytes))}</span>
                </div>
              {/each}
            </div>
          </div>
        </Disclosure>
      {:else}
        <section class="space-y-2">
          <h2 class="text-sm font-semibold text-text-primary">Files</h2>
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
      {#if status === ACQUISITION_STATUS.failed && detail.summary.statusMessage}
        <Alert.Root variant="destructive">
          <CircleAlert />
          <Alert.Title>{hasResumableImport ? "Import interrupted" : "Acquisition failed"}</Alert.Title>
          <Alert.Description>{detail.summary.statusMessage}</Alert.Description>
        </Alert.Root>
      {/if}
      {#if hasResumableImport && detail.candidates.length === 0}
        <StatePlaceholder
          icon={CloudDownload}
          title="Import can be resumed"
          description="Retry import continues from the last completed file. Start over removes this interrupted import and begins a new search."
        />
      {:else}
      <section class="space-y-3">
        <h2 class="flex items-baseline gap-2 text-sm font-semibold text-text-primary">
          Releases
          <span class="text-xs font-normal tabular-nums text-text-muted">{detail.candidates.length}</span>
        </h2>

        {#if canSearchReleases}
          <form
            class="flex flex-col gap-2 sm:flex-row"
            onsubmit={(event) => {
              event.preventDefault();
              void reSearch(customQuery);
            }}
          >
            <SearchInput
              bind:value={customQuery}
              ariaLabel="Custom release search term"
              placeholder="Try an exact title, edition, group, or quality…"
              loading={busy}
              class="min-w-0 flex-1"
            />
            <Button type="submit" variant="secondary" disabled={busy || !customQuery.trim()} class="gap-1.5">
              <Search class="h-3.5 w-3.5" />
              Search term
            </Button>
          </form>
        {/if}

        {#if detail.candidates.length === 0}
          <StatePlaceholder
            icon={SearchX}
            title="No releases found"
            description="No indexer returned a matching release for this title. You can upload a .torrent manually below."
          />
        {:else}
          <ReleaseTable candidates={detail.candidates} canChoose={canPickRelease} {busy} onQueue={queue} onBlocklist={blocklist} />
        {/if}

        {#if canSearchReleases}
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
      {/if}
    </Card.Content>

    {#if canRetryImport || canStartOver || canReSearch || canCancel}
      <Card.Footer class="flex flex-wrap justify-start gap-control-gap border-border-subtle">
        {#if canRetryImport}
          <Button
            type="button"
            variant="primary"
            disabled={busy}
            onclick={() => void retryImport(false)}
            title={status === ACQUISITION_STATUS.downloaded
                ? "Queue import again without removing the completed download."
                : "Resume the exact durable import plan from its last completed file."}
          >
            <CloudDownload data-icon="inline-start" />
            Retry import
          </Button>
        {/if}
        {#if canStartOver}
          <Button
            type="button"
            variant="danger"
            disabled={busy}
            onclick={() => (resetConfirmOpen = true)}
            title="Discard the interrupted import, its partial files, and the current download, then begin a clean search."
          >
            <RotateCcw data-icon="inline-start" />
            Start over
          </Button>
        {/if}
        {#if canReSearch}
          <Button type="button" variant="ghost" disabled={busy} onclick={() => void reSearch()}>
            <RefreshCw data-icon="inline-start" />
            Search again
          </Button>
        {/if}
        {#if canCancel}
          <Button type="button" variant="danger" disabled={busy} onclick={() => void cancel()}>
            <X data-icon="inline-start" />
            Cancel
          </Button>
        {/if}
      </Card.Footer>
    {/if}

    {#if history.length > 0}
      <Card.Footer class="block border-border-subtle bg-transparent p-0">
        <Disclosure
          title="History"
          icon={History}
          count={history.length}
          class="rounded-none border-0 bg-transparent"
        >
          <div class="min-w-0">
            <AcquisitionHistoryList entries={history} />
          </div>
        </Disclosure>
      </Card.Footer>
    {/if}
  </Card.Root>
{/if}

<ConfirmDialog
  open={overrideCandidate !== null}
  title="Replace the interrupted import?"
  message={overrideCandidate
    ? `Choosing “${overrideCandidate.title}” clears its partial files and current download, then downloads this selected release as your explicit override.`
    : ""}
  confirmLabel="Replace and download"
  danger
  onConfirm={confirmReleaseOverride}
  onClose={() => (overrideCandidate = null)}
/>

<ConfirmDialog
  open={resetConfirmOpen}
  title="Start this acquisition over?"
  message="This permanently deletes every file owned by the interrupted import, removes any remaining download data it can reach, clears the partial state, and starts a clean search for the still-Wanted item. Existing library files from before an upgrade are restored when a recovery copy exists."
  confirmLabel="Start over"
  danger
  onConfirm={startOver}
  onClose={() => (resetConfirmOpen = false)}
/>

<ConfirmDialog
  open={unsafeImportConfirmOpen}
  title="Import from this potentially unsafe download?"
  message={`${manualImportWarning ?? "This payload contains a potentially dangerous file."} Only the episode files you mapped will be imported. Confirm only after verifying that those media files are expected.`}
  confirmLabel="Import mapped episodes"
  danger
  onConfirm={importMappedFiles}
  onClose={() => (unsafeImportConfirmOpen = false)}
/>

<ConfirmDialog
  open={rejectConfirmOpen}
  title="Reject this downloaded release?"
  message="This removes the download and its data, blocklists this exact release so Prismedia will not grab it again, and immediately starts a fresh search for the still-Wanted item."
  confirmLabel="Reject"
  danger
  onConfirm={rejectManualImport}
  onClose={() => (rejectConfirmOpen = false)}
/>
