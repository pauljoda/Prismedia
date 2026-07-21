<script lang="ts">
  /**
   * THE acquisition + monitoring surface an entity page mounts — the Acquisition detail tab's body,
   * collapsing everything the request layer knows about an entity: its stable monitor,
   * the wanted placeholder's "Search for release", direct-child monitoring, and the full acquisition
   * management panel (releases, live download, files, cancel). All state lives in the
   * page-owned {@link useEntityAcquisition} composable, whose `visible` also gates the tab itself;
   * this component only renders it. Renders nothing while the state says there is no story.
   */
  import { Bell, BellRing, RefreshCw, Search, Trash2 } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { ACQUISITION_STATUS, ENTITY_KIND } from "$lib/api/generated/codes";
  import type { EntityCapability } from "$lib/api/generated/model";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";
  import ManualAcquisitionActions from "$lib/components/acquisitions/ManualAcquisitionActions.svelte";
  import EntityChildMonitoring from "$lib/components/acquisitions/EntityChildMonitoring.svelte";
  import type { EntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { acquisitionStatusShouldPoll } from "$lib/requests/acquisition-status";
  import EntityFileManagementAction from "$lib/components/entities/EntityFileManagementAction.svelte";
  import type { EntityFileManagementCallbacks } from "$lib/entities/entity-file-management";

  let {
    acq,
    entity,
    fileManagement,
    showEntityRequestControls = true,
    showAcquisitionPanel = true,
    onCancelled,
    onImported,
  }: {
    /** The page-owned acquisition state (from {@link useEntityAcquisition}). */
    acq: EntityAcquisition;
    /** Entity core projected into the shared managed-file action. */
    entity?: { id: string; title: string; kind?: string; capabilities: EntityCapability[] } | null;
    /** Route follow-ups after managed deletion either removes or reverts the Entity. */
    fileManagement?: EntityFileManagementCallbacks;
    /** False when an owner-specific surface provides its own monitor and request controls. */
    showEntityRequestControls?: boolean;
    /** False when a richer owner-specific surface renders the acquisition rows itself. */
    showAcquisitionPanel?: boolean;
    /**
     * Called after the acquisition is cancelled, so the page can refresh. Cancel stops the download
     * only — the wanted placeholder and any monitoring stay, and the page keeps existing.
     */
    onCancelled?: () => void;
    /** Called once when the acquisition becomes Imported so the page can refresh its Entity in place. */
    onImported?: () => void | Promise<void>;
  } = $props();

  const hasActions = $derived(
    acq.showSync ||
      (showEntityRequestControls && acq.showMonitor) ||
      (showEntityRequestControls && acq.showSearch) ||
      acq.showSearchMissing ||
      (acq.showFileManagement && Boolean(entity && fileManagement)),
  );
  const activeChildAcquisitionCount = $derived(acq.childCards.filter((card) =>
    acquisitionStatusShouldPoll(card.wantedStatus)
    || acquisitionStatusShouldPoll(card.latestAcquisitionStatus),
  ).length);
  const failedParentWithChildActivity = $derived(
    acq.acquisition?.summary.status === ACQUISITION_STATUS.failed
      && activeChildAcquisitionCount > 0,
  );
  const replaceableKind = $derived(
    entity?.kind === ENTITY_KIND.book
      || entity?.kind === ENTITY_KIND.movie
      || entity?.kind === ENTITY_KIND.video
      || entity?.kind === ENTITY_KIND.audioLibrary,
  );
  const uploadableAcquisitionKind = $derived(
    replaceableKind
      || entity?.kind === ENTITY_KIND.audioLibrary
      || entity?.kind === ENTITY_KIND.videoSeason,
  );
  const hasImportedBaseline = $derived(
    acq.acquisition?.summary.status === ACQUISITION_STATUS.imported,
  );
  const hasOwnedContent = $derived(acq.showFileManagement || hasImportedBaseline);
  const activeChildLabel = $derived(
    acq.childCards.every((card) => card.entity.kind === ENTITY_KIND.video)
      ? activeChildAcquisitionCount === 1 ? "episode" : "episodes"
      : activeChildAcquisitionCount === 1 ? "child item" : "child items",
  );
</script>

{#if acq.visible}
  <section class="acquisition-card">
    {#if hasActions}
      <div class="flex flex-wrap items-center gap-2">
        {#if acq.showSync}
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={acq.syncBusy}
            onclick={() => void acq.syncNow()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title="Re-sync from the provider now instead of waiting for the daily sweep"
          >
            <RefreshCw class="h-3.5 w-3.5" />
            {acq.syncBusy ? "Checking…" : "Check for new works"}
          </Button>
        {/if}
        {#if showEntityRequestControls && acq.showMonitor}
          <Button
            type="button"
            variant={acq.monitorActive || acq.monitorDeletingFiles ? "primary" : "secondary"}
            size="sm"
            disabled={acq.monitorBusy || acq.monitorDeletingFiles || acq.monitorUnknownStatus}
            onclick={() => void acq.toggleMonitor()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title={acq.monitorStopping
              ? "Cleanup did not finish; retry unmonitoring"
              : acq.monitorDeletingFiles
                ? "Managed file deletion is in progress; retry from Delete files if needed"
                : acq.monitorUnknownStatus
                  ? "Monitoring is locked until this status can be refreshed"
              : acq.monitorActive
                ? "Monitoring this item — click to stop and clear its off-disk acquisition state"
                : "Monitor this item and manage its off-disk acquisition state"}
          >
            {#if acq.monitorStopping}
              <RefreshCw class="h-3.5 w-3.5" />
              Finish unmonitoring
            {:else if acq.monitorDeletingFiles}
              <Trash2 class="h-3.5 w-3.5" />
              Deleting files…
            {:else if acq.monitorUnknownStatus}
              <RefreshCw class="h-3.5 w-3.5" />
              Updating…
            {:else if acq.monitorActive}
              <BellRing class="h-3.5 w-3.5" />
              Monitoring
            {:else}
              <Bell class="h-3.5 w-3.5" />
              {acq.monitor ? "Resume monitoring" : "Monitor"}
            {/if}
          </Button>
        {/if}
        {#if showEntityRequestControls && acq.showSearch}
          <Button
            type="button"
            variant="primary"
            size="sm"
            disabled={acq.searchBusy}
            onclick={() => void acq.searchForRelease()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
          >
            <Search class="h-3.5 w-3.5" />
            {acq.searchBusy ? "Searching…" : "Search for release"}
          </Button>
        {/if}
        {#if acq.showSearchMissing}
          <Button
            type="button"
            variant="primary"
            size="sm"
            disabled={acq.missingBusy}
            onclick={() => void acq.searchMissing()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title="Sweep for anything missing at any depth — every gap gets its own monitored search"
          >
            <Search class="h-3.5 w-3.5" />
            {acq.missingBusy
              ? "Searching…"
              : acq.missingChildCount > 0
                ? `Search ${acq.missingChildCount} missing`
                : "Search missing content"}
          </Button>
        {/if}
        {#if acq.showFileManagement && entity && fileManagement}
          <EntityFileManagementAction
            {entity}
            onDeleted={fileManagement.onDeleted}
            onReverted={fileManagement.onReverted}
            compact
          />
        {/if}
      </div>
    {/if}

    {#if acq.missingResult}
      <p class="text-[0.72rem] text-text-muted">{acq.missingResult}</p>
    {/if}

    {#if acq.monitorError}
      <p role="alert" class="text-[0.72rem] text-error-text">{acq.monitorError}</p>
    {/if}

    {#if showEntityRequestControls && acq.showMonitor && acq.trackedVia}
      <p class="text-[0.72rem] text-text-muted">
        {acq.monitorActive && acq.showSync
          ? `Watching for new works daily via ${acq.trackedVia}.`
          : acq.monitorDeletingFiles
            ? `Monitoring stays enabled via ${acq.trackedVia} while files are deleted.`
          : acq.monitorActive
            ? `Monitoring via ${acq.trackedVia}.`
            : `Monitoring available — tracked via ${acq.trackedVia}.`}
      </p>
    {/if}

    {#if showEntityRequestControls && acq.showSearch}
      <p class="text-[0.72rem] text-text-muted">
        No file yet. Searching starts an auto-grabbing, monitored acquisition for this item.
      </p>
    {/if}

    {#if entity && uploadableAcquisitionKind}
      <ManualAcquisitionActions
        entityId={entity.id}
        canReplace={hasOwnedContent && replaceableKind}
        canUpload={Boolean(acq.acquisition) || (hasOwnedContent && replaceableKind)}
        onStarted={async (detail) => {
          acq.acquisition = detail;
          await acq.refresh();
        }}
      />
    {/if}

    {#if acq.childCards.length > 0}
      <EntityChildMonitoring
        cards={acq.childCards}
        onChanged={acq.childMonitoringChanged}
      />
    {/if}

    {#if showAcquisitionPanel && acq.acquisition}
      {#if failedParentWithChildActivity}
        <details class="parent-attempt">
          <summary>
            Parent release attempt failed
            <span>{activeChildAcquisitionCount} {activeChildLabel} active instead</span>
          </summary>
          <div class="parent-attempt-body">
            {#key acq.acquisition.summary.id}
              <AcquisitionPanel
                acquisitionId={acq.acquisition.summary.id}
                bind:detail={acq.acquisition}
                {onCancelled}
                {onImported}
                onReset={acq.refresh}
              />
            {/key}
          </div>
        </details>
      {:else}
        {#key acq.acquisition.summary.id}
          <AcquisitionPanel
            acquisitionId={acq.acquisition.summary.id}
            bind:detail={acq.acquisition}
            {onCancelled}
            {onImported}
            onReset={acq.refresh}
          />
        {/key}
      {/if}
    {/if}
  </section>

{/if}

<style>
  /* Frameless: the detail tab panel supplies the surface, border, and padding. */
  .acquisition-card {
    display: grid;
    gap: 0.9rem;
    min-width: 0;
  }
  .parent-attempt {
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: var(--color-surface-1);
  }
  .parent-attempt summary {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem 1rem;
    padding: 0.7rem 0.8rem;
    color: var(--color-text-secondary);
    font-size: 0.76rem;
    font-weight: 600;
    cursor: pointer;
  }
  .parent-attempt summary span {
    color: var(--color-text-accent);
    font-family: var(--font-mono);
    font-size: 0.66rem;
    font-weight: 500;
  }
  .parent-attempt-body {
    padding: 0 0.8rem 0.8rem;
  }

</style>
